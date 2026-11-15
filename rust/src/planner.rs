//! The planner: searches only over our own moves, using the engine's exact replies, for the
//! shortest win. A port of exploit/Planner.cs.
//!
//! The game's engine is deterministic, so a line "our move, its reply, our move, ..." is fully
//! determined by our moves, and the search tree only branches on our side. A best-first search
//! with a time budget explores it:
//!  * every queue entry is (position, our candidate move), prioritised by a cheap estimate,
//!  * popping an entry runs the engine model for the actual reply. That is the expensive step,
//!    so it is done lazily, only for entries that reach the front,
//!  * a line where the opponent has no legal move after our move is a win (mate and stalemate
//!    both lose in Really Bad Chess). Once a win in N is known, only shorter lines are explored,
//!  * a found win is kept as a plan across turns: the model is exact, so the plan stays valid
//!    as long as the opponent replies as predicted, and later turns only look for something shorter.
//!
//! If no win is found in time, it plays towards the best position found in the tree.

use crate::board::FastBoard;
use std::cmp::Ordering;
use std::collections::{BinaryHeap, HashMap};
use std::sync::{Arc, Condvar, Mutex};

/// Position identity for the reply cache: squares, side to move, castling state, piece count.
#[derive(Clone, Copy, PartialEq, Eq, Hash)]
struct StateKey([u64; 4], u32);

impl StateKey {
    fn of(p: &FastBoard) -> Self {
        let mut x = [0u64; 4];
        for (i, &s) in p.sq.iter().enumerate() {
            x[i / 16] |= ((s + 6) as u64) << (4 * (i % 16));
        }
        let f = p.white_turn as u32
            | (p.w_castled as u32) << 1
            | (p.b_castled as u32) << 2
            | ((p.wa > 0) as u32) << 3
            | ((p.wh > 0) as u32) << 4
            | ((p.ba > 0) as u32) << 5
            | ((p.bh > 0) as u32) << 6
            | ((p.wk > 0) as u32) << 7
            | ((p.bk > 0) as u32) << 8
            | (p.remaining as u32) << 16;
        StateKey(x, f)
    }
}

pub use crate::board::FMove;

const WIN: i32 = 1_000_000;

/// Seconds since an arbitrary start, for the time budget.
pub fn now_secs() -> f64 {
    #[cfg(all(target_arch = "wasm32", target_os = "unknown", feature = "wasm"))]
    {
        js_sys::Date::now() / 1000.0
    }
    #[cfg(all(target_arch = "wasm32", target_os = "unknown", not(feature = "wasm")))]
    {
        0.0 // no clock: only max_nodes limits the search
    }
    #[cfg(not(all(target_arch = "wasm32", target_os = "unknown")))]
    {
        use std::sync::OnceLock;
        use std::time::Instant;
        static START: OnceLock<Instant> = OnceLock::new();
        START.get_or_init(Instant::now).elapsed().as_secs_f64()
    }
}

struct Node {
    pos: FastBoard, // our side to move
    parent: Option<Arc<Node>>,
    ours: FMove,  // how we got here from parent
    reply: FMove, //
    depth: i32,   // our moves from the root
    root_idx: usize,
}

struct Entry {
    prio: i64, // lower first, like .NET's PriorityQueue
    parent: Arc<Node>,
    mv: FMove,
    root_idx: usize,
    est: i32,
}
impl PartialEq for Entry {
    fn eq(&self, o: &Self) -> bool {
        self.prio == o.prio
    }
}
impl Eq for Entry {}
impl PartialOrd for Entry {
    fn partial_cmp(&self, o: &Self) -> Option<Ordering> {
        Some(self.cmp(o))
    }
}
impl Ord for Entry {
    fn cmp(&self, o: &Self) -> Ordering {
        o.prio.cmp(&self.prio) // BinaryHeap is a max-heap
    }
}

struct Shared {
    queue: BinaryHeap<Entry>,
    seq: i64,
    penalty: i32,
    best_win: i32,
    best_line: Option<Vec<(FMove, FMove)>>,
    best_by_root: Vec<i32>,
    busy: i32,
    expanded: usize,
}

pub struct Planner {
    /// search budget per move
    pub seconds_per_move: f64,
    /// stop after this many expanded nodes (None: time only). Makes runs reproducible.
    pub max_nodes: Option<usize>,
    /// worker threads (1 on wasm32 without thread support)
    pub threads: usize,
    /// the opponent's search depths (the game ships 3/5/5)
    pub opp_weak: u8,
    pub opp_strong: u8,
    pub opp_end: u8,
    /// priority cost per extra move of ours (centipawns)
    pub depth_penalty: i32,
    /// once a win is known: look for shorter ones more broadly
    pub depth_penalty_after_win: i32,
    /// bonus per opponent legal move fewer than 30
    pub mobility_weight: i32,
    pub max_depth: i32,

    replies: Mutex<HashMap<StateKey, Option<FMove>>>,
    model_calls: std::sync::atomic::AtomicU64,
    plan: Option<Vec<(FMove, FMove)>>,
    pub last_info: String,
}

impl Default for Planner {
    fn default() -> Self {
        Planner {
            seconds_per_move: 300.0,
            max_nodes: None,
            threads: default_threads(),
            opp_weak: 3,
            opp_strong: 5,
            opp_end: 5,
            depth_penalty: 100,
            depth_penalty_after_win: 400,
            mobility_weight: 8,
            max_depth: 40,
            replies: Mutex::new(HashMap::new()),
            model_calls: 0.into(),
            plan: None,
            last_info: String::new(),
        }
    }
}

fn default_threads() -> usize {
    #[cfg(all(target_arch = "wasm32", not(target_feature = "atomics")))]
    {
        1
    }
    #[cfg(not(all(target_arch = "wasm32", not(target_feature = "atomics"))))]
    {
        std::thread::available_parallelism().map(|n| n.get()).unwrap_or(1)
    }
}

/// Result of Planner::choose
#[derive(Clone, Copy, Debug)]
pub struct Choice {
    pub mv: FMove,
    /// Some(n): a forced win in n of our moves (counting this one) was found
    pub win_in: Option<i32>,
}

impl Planner {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn model_calls(&self) -> u64 {
        self.model_calls.load(std::sync::atomic::Ordering::Relaxed)
    }

    /// The engine's reply in `p` (opponent to move), cached.
    pub fn reply(&self, p: &FastBoard) -> Option<FMove> {
        let k = StateKey::of(p);
        if let Some(r) = self.replies.lock().unwrap().get(&k) {
            return *r;
        }
        self.model_calls.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let r = p.engine_move(self.opp_weak, self.opp_strong, self.opp_end);
        self.replies.lock().unwrap().insert(k, r);
        r
    }

    /// Call after both our move and the opponent's reply were played.
    pub fn observe(&mut self, ours: FMove, reply: FMove) {
        let keep = match &mut self.plan {
            Some(plan) if !plan.is_empty() && same(&plan[0].0, &ours) && same(&plan[0].1, &reply) => {
                plan.remove(0);
                true
            }
            _ => false,
        };
        if !keep {
            self.plan = None;
        }
    }

    /// Choose our move. `pos` must be our turn and carry the last move (for en passant).
    pub fn choose(&mut self, pos: &FastBoard) -> Option<Choice> {
        let t0 = now_secs();
        let us = pos.white_turn;
        let mut root_pos = pos.clone_with(true);
        let root_moves = root_pos.legal_moves();
        if root_moves.is_empty() {
            return None;
        }
        let mut sh = Shared {
            queue: BinaryHeap::new(),
            seq: 0,
            penalty: self.depth_penalty,
            best_win: i32::MAX,
            best_line: None,
            best_by_root: vec![i32::MIN; root_moves.len()],
            busy: 0,
            expanded: 0,
        };
        // continue an existing plan if the opponent did what we expected
        if let Some(plan) = &self.plan {
            if !plan.is_empty() && root_moves.iter().any(|m| same(m, &plan[0].0)) {
                sh.best_win = plan.len() as i32;
                sh.best_line = Some(plan.clone());
                sh.penalty = self.depth_penalty_after_win;
            }
        }
        let root = Arc::new(Node { pos: root_pos, parent: None, ours: FMove::default(), reply: FMove::default(), depth: 0, root_idx: 0 });
        let gate = (Mutex::new(sh), Condvar::new());
        let this = &*self;

        this.expand(&gate, &root, Some(&root_moves), us);
        let budget = self.seconds_per_move;
        let threads = self.threads.max(1);
        if gate.0.lock().unwrap().best_win > 1 {
            let work = || this.worker(&gate, us, t0, budget);
            #[cfg(not(all(target_arch = "wasm32", not(target_feature = "atomics"))))]
            if threads > 1 {
                std::thread::scope(|s| {
                    for _ in 0..threads {
                        s.spawn(work);
                    }
                });
            } else {
                work();
            }
            #[cfg(all(target_arch = "wasm32", not(target_feature = "atomics")))]
            {
                let _ = threads;
                work();
            }
        }

        let sh = gate.0.into_inner().unwrap();
        let (choice, win_in);
        if let Some(line) = sh.best_line {
            choice = line[0].0;
            win_in = Some(sh.best_win);
            self.plan = Some(line);
        } else {
            let mut bi = 0;
            for i in 1..root_moves.len() {
                if sh.best_by_root[i] > sh.best_by_root[bi] {
                    bi = i;
                }
            }
            choice = root_moves[bi];
            win_in = None;
            self.plan = None;
        }
        self.last_info = format!(
            "{} after {:.0}s, {} nodes, {} model calls total",
            match win_in {
                Some(n) => format!("win in {n}"),
                None => "no win".into(),
            },
            now_secs() - t0,
            sh.expanded,
            self.model_calls()
        );
        Some(Choice { mv: choice, win_in })
    }

    fn prio(sh: &mut Shared, est: i32, depth: i32) -> i64 {
        let p = -((est - sh.penalty * depth) as i64) * 4096 + (sh.seq & 4095);
        sh.seq += 1;
        p
    }

    /// a win: `last` leaves the opponent with no move
    fn record(&self, sh: &mut Shared, n: &Arc<Node>, last: FMove, depth: i32) {
        if depth >= sh.best_win {
            return;
        }
        let mut line = vec![(last, FMove::default())];
        let mut x = n;
        while let Some(p) = &x.parent {
            line.insert(0, (x.ours, x.reply));
            x = p;
        }
        sh.best_win = depth;
        sh.best_line = Some(line);
        if sh.penalty != self.depth_penalty_after_win {
            sh.penalty = self.depth_penalty_after_win;
            let items: Vec<Entry> = std::mem::take(&mut sh.queue).into_vec();
            for mut it in items {
                it.prio = Self::prio(sh, it.est, it.parent.depth + 1);
                sh.queue.push(it);
            }
        }
    }

    /// Estimate our candidate moves at `n` and queue them.
    fn expand(&self, gate: &(Mutex<Shared>, Condvar), n: &Arc<Node>, root_moves: Option<&[FMove]>, us: bool) {
        let mut scratch = n.pos.clone_with(true);
        let owned;
        let moves = match root_moves {
            Some(m) => m,
            None => {
                owned = scratch.legal_moves();
                &owned
            }
        };
        for (i, m) in moves.iter().enumerate() {
            let ri = if root_moves.is_some() { i } else { n.root_idx };
            if n.depth + 1 >= gate.0.lock().unwrap().best_win {
                return;
            }
            let mut p = n.pos.clone_with(true);
            p.make(m);
            let opp_moves = if king_gone(&p, !us) { 0 } else { p.legal_moves().len() as i32 };
            if opp_moves == 0 {
                let mut sh = gate.0.lock().unwrap();
                self.record(&mut sh, n, *m, n.depth + 1);
                continue;
            }
            if n.depth + 2 > self.max_depth {
                continue;
            }
            let est = -qsearch(&mut p, -WIN, WIN, !us, 0) + (30 - opp_moves).max(0) * self.mobility_weight;
            let mut sh = gate.0.lock().unwrap();
            let prio = Self::prio(&mut sh, est, n.depth + 1);
            sh.queue.push(Entry { prio, parent: n.clone(), mv: *m, root_idx: ri, est });
            if root_moves.is_some() {
                sh.best_by_root[ri] = sh.best_by_root[ri].max(est - self.depth_penalty);
            }
        }
    }

    fn worker(&self, gate: &(Mutex<Shared>, Condvar), us: bool, t0: f64, budget: f64) {
        loop {
            if now_secs() - t0 >= budget {
                return;
            }
            let e = {
                let mut sh = gate.0.lock().unwrap();
                if sh.best_win <= 2 {
                    return; // wins in 1 were all checked when the root was expanded
                }
                if let Some(limit) = self.max_nodes {
                    if sh.expanded >= limit {
                        return;
                    }
                }
                match sh.queue.pop() {
                    None => {
                        if sh.busy == 0 {
                            return; // nothing queued and nobody can add more
                        }
                        let _ = gate.1.wait_timeout(sh, std::time::Duration::from_millis(20)).unwrap();
                        continue;
                    }
                    Some(e) => {
                        if e.parent.depth + 2 > sh.best_win {
                            continue; // cannot beat the known win
                        }
                        sh.busy += 1;
                        e
                    }
                }
            };
            self.visit(gate, e, us);
            let mut sh = gate.0.lock().unwrap();
            sh.busy -= 1;
            gate.1.notify_all();
        }
    }

    fn visit(&self, gate: &(Mutex<Shared>, Condvar), e: Entry, us: bool) {
        let mut p = e.parent.pos.clone_with(true);
        p.make(&e.mv);
        let reply = match self.reply(&p) {
            Some(r) => r,
            None => {
                let mut sh = gate.0.lock().unwrap();
                self.record(&mut sh, &e.parent, e.mv, e.parent.depth + 1);
                return;
            }
        };
        p.make(&reply);
        if king_gone(&p, us) || p.legal_moves().is_empty() {
            return; // we lose on this line
        }
        let depth = e.parent.depth + 1;
        let score = qsearch(&mut p, -WIN, WIN, us, 0) - self.depth_penalty * depth;
        let n = Arc::new(Node { pos: p, parent: Some(e.parent), ours: e.mv, reply, depth, root_idx: e.root_idx });
        {
            let mut sh = gate.0.lock().unwrap();
            if score > sh.best_by_root[n.root_idx] {
                sh.best_by_root[n.root_idx] = score;
            }
            sh.expanded += 1;
        }
        self.expand(gate, &n, None, us);
    }
}

fn same(a: &FMove, b: &FMove) -> bool {
    a.from == b.from && a.to == b.to
}

fn king_gone(p: &FastBoard, white: bool) -> bool {
    let k = if white { 6 } else { -6 };
    !p.sq.contains(&k)
}

// ---------------------------------------------------------------- evaluation
const VAL: [i32; 7] = [0, 100, 500, 300, 310, 900, 0];

/// Quiescence search over captures, using the game's own move legality.
fn qsearch(p: &mut FastBoard, mut alpha: i32, beta: i32, side: bool, ply: i32) -> i32 {
    let stand = eval(p, side);
    if ply >= 6 || stand >= beta {
        return stand;
    }
    if stand > alpha {
        alpha = stand;
    }
    let ms = p.legal_moves();
    if ms.is_empty() {
        return -WIN / 2; // side to move has no move: loses
    }
    let mut order: Vec<usize> = Vec::with_capacity(ms.len());
    for (i, m) in ms.iter().enumerate() {
        let v = p.sq[m.to as usize];
        if v == 6 || v == -6 {
            return WIN / 2; // king capture available
        }
        if v != 0 || m.kind == 2 {
            order.push(i);
        }
    }
    // most valuable victim first (insertion sort, stable)
    let victim = |p: &FastBoard, i: usize| VAL[p.sq[ms[i].to as usize].unsigned_abs() as usize];
    for a in 1..order.len() {
        let mut b = a;
        while b > 0 && victim(p, order[b]) > victim(p, order[b - 1]) {
            order.swap(b, b - 1);
            b -= 1;
        }
    }
    for &k in &order {
        p.make(&ms[k]);
        let v = -qsearch(p, -beta, -alpha, !side, ply + 1);
        p.revert();
        if v >= beta {
            return v;
        }
        if v > alpha {
            alpha = v;
        }
    }
    alpha
}

/// Static score from `side`'s point of view: material, plus mating terms when ahead.
pub fn eval(p: &FastBoard, side: bool) -> i32 {
    let (mut mat, mut wk, mut bk) = (0, -1i32, -1i32);
    for i in 0..64 {
        let s = p.sq[i] as i32;
        if s == 0 {
            continue;
        }
        let t = s.abs();
        if t == 6 {
            if s > 0 {
                wk = i as i32;
            } else {
                bk = i as i32;
            }
            continue;
        }
        let mut v = VAL[t as usize];
        if t == 1 {
            let r = i as i32 / 8;
            let adv = if s > 0 { 6 - r } else { r - 1 };
            v += adv * adv * 3;
        }
        mat += if s > 0 { v } else { -v };
    }
    if wk < 0 {
        return if side { -WIN / 2 } else { WIN / 2 };
    }
    if bk < 0 {
        return if side { WIN / 2 } else { -WIN / 2 };
    }
    let mut score = mat;
    let free_b = king_freedom(p, bk, false);
    let free_w = king_freedom(p, wk, true);
    if mat > 0 {
        score += (8 - free_b) * 25 + (6 - center(bk)) * -8 + (14 - dist(wk, bk)) * 3;
    } else if mat < 0 {
        score -= (8 - free_w) * 25 + (6 - center(wk)) * -8 + (14 - dist(wk, bk)) * 3;
    }
    if side {
        score
    } else {
        -score
    }
}

/// king-adjacent squares not attacked by the enemy and not blocked by own pieces
fn king_freedom(p: &FastBoard, k: i32, white: bool) -> i32 {
    let (kr, kf) = (k / 8, k % 8);
    let mut free = 0;
    for dr in -1..=1 {
        for df in -1..=1 {
            if dr == 0 && df == 0 {
                continue;
            }
            let (r, f) = (kr + dr, kf + df);
            if !(0..=7).contains(&r) || !(0..=7).contains(&f) {
                continue;
            }
            let s = p.sq[(r * 8 + f) as usize];
            if s != 0 && (s > 0) == white {
                continue;
            }
            if !attacked(p, r * 8 + f, !white, k) {
                free += 1;
            }
        }
    }
    free
}

const KN: [i32; 8] = [-17, -15, -10, -6, 6, 10, 15, 17];

fn attacked(p: &FastBoard, sq: i32, by_white: bool, ignore: i32) -> bool {
    let (r, f) = (sq / 8, sq % 8);
    let pc: i8 = if by_white { 1 } else { -1 };
    // white pawns attack towards index 0
    let pr = if by_white { r + 1 } else { r - 1 };
    if (0..=7).contains(&pr) {
        if f > 0 && p.sq[(pr * 8 + f - 1) as usize] == pc {
            return true;
        }
        if f < 7 && p.sq[(pr * 8 + f + 1) as usize] == pc {
            return true;
        }
    }
    for d in KN {
        let t = sq + d;
        if !(0..=63).contains(&t) || (t % 8 - f).abs() > 2 {
            continue;
        }
        if p.sq[t as usize] == 3 * pc {
            return true;
        }
    }
    for dr in -1..=1 {
        for df in -1..=1 {
            if dr == 0 && df == 0 {
                continue;
            }
            let diag = dr != 0 && df != 0;
            let (mut rr, mut ff, mut dist) = (r + dr, f + df, 1);
            while (0..=7).contains(&rr) && (0..=7).contains(&ff) {
                let t = rr * 8 + ff;
                let s = p.sq[t as usize];
                if t != ignore && s != 0 {
                    if s == 5 * pc || (if diag { s == 4 * pc } else { s == 2 * pc }) || (dist == 1 && s == 6 * pc) {
                        return true;
                    }
                    break;
                }
                rr += dr;
                ff += df;
                dist += 1;
            }
        }
    }
    false
}

fn center(sq: i32) -> i32 {
    let (f, r) = (sq % 8, sq / 8);
    (3 - f).max(f - 4) + (3 - r).max(r - 4)
}
fn dist(a: i32, b: i32) -> i32 {
    (a % 8 - b % 8).abs() + (a / 8 - b / 8).abs()
}
