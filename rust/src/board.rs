//! The game's board and engine: a port of FastBoard in exploit/FastModel.cs, which is itself a
//! transcription of the decompiled cgBoard / cgEngine (src/) with the allocations removed. It must
//! pick exactly the game's moves, so every quirk is kept, with a NOTE where it matters.
//!
//! Board index 0 = a8 ... 63 = h1. Pieces 1 P, 2 R, 3 N, 4 B, 5 Q, 6 K; negative = black.

use crate::sets::{self, key, POS_BISHOP, POS_KNIGHT, POS_PAWN, POS_ROOK};
use crate::sort::dotnet_sort;
use std::fmt;

pub const NONE: u8 = 255;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct FMove {
    pub from: u8,
    pub to: u8,
    pub pv: i8,
    /// 0 normal, 1 castling, 2 en passant, 3 null move (65,65)
    pub kind: u8,
    /// castling: rook from (a), rook to (b). En passant: square of the captured pawn in a.
    pub a: u8,
    pub b: u8,
    pub val: i32,
}

impl FMove {
    pub const fn new(from: u8, to: u8, pv: i8) -> Self {
        FMove { from, to, pv, kind: 0, a: 0, b: 0, val: 0 }
    }
    const fn special(from: u8, to: u8, pv: i8, kind: u8, a: u8, b: u8) -> Self {
        FMove { from, to, pv, kind, a, b, val: 0 }
    }
    pub fn is_null(&self) -> bool {
        self.to == 65
    }
}

impl fmt::Display for FMove {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{}->{}", square_name(self.from), square_name(self.to))
    }
}

pub fn square_name(i: u8) -> String {
    if i >= 64 {
        return "--".into();
    }
    format!("{}{}", (b'a' + i % 8) as char, 8 - i / 8)
}

const NULL_MOVE: FMove = FMove::special(65, 65, 0, 3, 0, 0);

static TERM: std::sync::OnceLock<Vec<i32>> = std::sync::OnceLock::new();
/// Material + piece-square value of each (piece, square), as cgBoardEval.Evaluate counts it.
fn term() -> &'static [i32] {
    TERM.get_or_init(|| {
        let mut t = vec![0i32; 13 * 64];
        for i in 0..64 {
            let (b, n, r, p) = (POS_BISHOP[i] as i32, POS_KNIGHT[i] as i32, POS_ROOK[i] as i32, POS_PAWN[i] as i32);
            t[i] = -50000;
            t[64 + i] = -9000;
            t[2 * 64 + i] = -(3050 + b);
            t[3 * 64 + i] = -(3000 + n);
            t[4 * 64 + i] = -(5000 + r);
            t[5 * 64 + i] = -(1000 + p);
            t[7 * 64 + i] = 1000 + p;
            t[8 * 64 + i] = 5000 + r;
            t[9 * 64 + i] = 3000 + n;
            t[10 * 64 + i] = 3050 + b;
            t[11 * 64 + i] = 9000;
            t[12 * 64 + i] = 50000;
        }
        t
    })
}

#[inline]
fn tv(t: &[i32], piece: i8, sq: usize) -> i32 {
    t[(piece as i32 + 6) as usize * 64 + sq]
}

#[derive(Clone, Copy)]
struct Hist {
    m: FMove,
    cap: i8,
    queened: bool,
}

pub struct FastBoard {
    pub sq: [i8; 64],
    mat: i32,
    pub w_castled: bool,
    pub b_castled: bool,
    pub white_turn: bool,
    pub wa: u8,
    pub wh: u8,
    pub ba: u8,
    pub bh: u8,
    pub wk: u8,
    pub bk: u8,
    pub remaining: u8,
    hist: Vec<Hist>,
    pool: Vec<Vec<FMove>>,
    ray: Vec<u8>,
    block: Vec<u8>,
    memo: IntMap,
}

impl Default for FastBoard {
    fn default() -> Self {
        FastBoard {
            sq: [0; 64],
            mat: 0,
            w_castled: false,
            b_castled: false,
            white_turn: true,
            wa: 0,
            wh: 0,
            ba: 0,
            bh: 0,
            wk: 0,
            bk: 0,
            remaining: 32,
            hist: Vec::with_capacity(64),
            pool: vec![],
            ray: Vec::with_capacity(16),
            block: Vec::with_capacity(16),
            memo: IntMap::empty(),
        }
    }
}

impl Clone for FastBoard {
    fn clone(&self) -> Self {
        self.clone_with(true)
    }
}

impl FastBoard {
    /// A board with these squares, white to move, no history: a freshly generated game.
    pub fn from_squares(sq: [i8; 64]) -> Self {
        let mut f = FastBoard { sq, ..Default::default() };
        f.init_mat();
        f
    }

    fn init_mat(&mut self) {
        let t = term();
        self.mat = (0..64).map(|i| tv(t, self.sq[i], i)).sum();
    }

    #[inline]
    fn set(&mut self, i: usize, v: i8) {
        let t = term();
        self.mat += tv(t, v, i) - tv(t, self.sq[i], i);
        self.sq[i] = v;
    }

    /// Copy of the position. With history, only the last move is kept (enough for en passant);
    /// without, none (like cgBoard.copyTo, which the engine searches from).
    pub fn clone_with(&self, with_history: bool) -> Self {
        let mut f = FastBoard {
            sq: self.sq,
            mat: self.mat,
            w_castled: self.w_castled,
            b_castled: self.b_castled,
            white_turn: self.white_turn,
            wa: self.wa,
            wh: self.wh,
            ba: self.ba,
            bh: self.bh,
            wk: self.wk,
            bk: self.bk,
            remaining: self.remaining,
            ..Default::default()
        };
        if with_history {
            if let Some(l) = self.hist.last() {
                f.hist.push(Hist { m: l.m, cap: 0, queened: false });
            }
        }
        f
    }

    pub fn last(&self) -> Option<FMove> {
        self.hist.last().map(|h| h.m)
    }

    /// Set the previous move (only from/to matter: they enable en passant).
    pub fn set_last(&mut self, from: u8, to: u8) {
        self.hist.clear();
        self.hist.push(Hist { m: FMove::new(from, to, 0), cap: 0, queened: false });
    }

    fn rent(&mut self) -> Vec<FMove> {
        let mut v = self.pool.pop().unwrap_or_else(|| Vec::with_capacity(64));
        v.clear();
        v
    }
    fn give(&mut self, v: Vec<FMove>) {
        self.pool.push(v);
    }

    // ------------------------------------------------------------------ move / revert
    pub fn make(&mut self, m: &FMove) {
        let mut h = Hist { m: *m, cap: 0, queened: false };
        if m.to == 65 {
            self.hist.push(h);
            return;
        }
        let (from, to) = (m.from as usize, m.to as usize);
        let mut cap = self.sq[to];
        if cap != 0 {
            self.remaining = self.remaining.wrapping_sub(1);
        }
        self.set(to, self.sq[from]);
        self.set(from, 0);
        if to <= 7 {
            if self.sq[to] == 1 {
                h.queened = true;
                self.set(to, 5);
            }
        } else if to >= 56 && self.sq[to] == -1 {
            h.queened = true;
            self.set(to, -5);
        }
        if m.kind == 1 {
            self.set(m.b as usize, self.sq[m.a as usize]);
            self.set(m.a as usize, 0);
            if self.white_turn {
                self.w_castled = true;
            } else {
                self.b_castled = true;
            }
        } else if m.kind == 2 {
            cap = self.sq[m.a as usize];
            self.set(m.a as usize, 0); // NOTE: remaining is not decremented
        }
        h.cap = cap;
        let p = self.sq[to];
        if p == 2 {
            if m.from == 63 {
                self.wh = self.wh.wrapping_add(1);
            } else if m.from == 56 {
                self.wa = self.wa.wrapping_add(1);
            }
        } else if p == -2 {
            if m.from == 7 {
                self.bh = self.bh.wrapping_add(1);
            } else if m.from == 0 {
                self.ba = self.ba.wrapping_add(1);
            }
        }
        if p == -6 {
            self.bk = self.bk.wrapping_add(1);
        } else if p == 6 {
            self.wk = self.wk.wrapping_add(1);
        }
        self.white_turn = !self.white_turn;
        self.hist.push(h);
    }

    pub fn revert(&mut self) {
        let h = self.hist.pop().expect("revert without a move");
        let m = h.m;
        if h.cap != 0 {
            self.remaining = self.remaining.wrapping_add(1);
        }
        if m.to == 65 {
            return;
        }
        let (from, to) = (m.from as usize, m.to as usize);
        self.set(from, self.sq[to]);
        if m.kind == 2 {
            self.set(m.a as usize, h.cap);
            self.set(to, 0);
        } else {
            self.set(to, h.cap);
        }
        if h.queened {
            self.set(from, if m.from < 31 { 1 } else { -1 });
        }
        let p = self.sq[from];
        if p == -2 && m.from == 0 {
            self.ba = self.ba.wrapping_sub(1);
        } else if p == -2 && m.from == 7 {
            self.bh = self.bh.wrapping_sub(1);
        } else if p == 2 && m.from == 56 {
            self.wa = self.wa.wrapping_sub(1);
        } else if p == 2 && m.from == 63 {
            self.wh = self.wh.wrapping_sub(1);
        }
        if p == 6 {
            self.wk = self.wk.wrapping_sub(1);
        } else if p == -6 {
            self.bk = self.bk.wrapping_sub(1);
        }
        if m.kind == 1 {
            self.set(m.a as usize, self.sq[m.b as usize]);
            self.set(m.b as usize, 0);
            // NOTE: clears the flag of the side now to move, not the side that castled
            if self.white_turn {
                self.b_castled = false;
            } else {
                self.w_castled = false;
            }
        }
        self.white_turn = !self.white_turn;
    }

    // ------------------------------------------------------------------ evaluation / key
    pub fn evaluate(&self) -> i32 {
        let mut v = self.mat;
        if self.wk == 0 && self.wa == 0 && !self.w_castled {
            v += 15;
        }
        if self.bk == 0 && self.ba == 0 && !self.b_castled {
            v -= 15;
        }
        if self.wk == 0 && self.wh == 0 && !self.w_castled {
            v += 22;
        }
        if self.bk == 0 && self.bh == 0 && !self.b_castled {
            v -= 22;
        }
        if self.b_castled {
            v -= 85;
        }
        if self.w_castled {
            v += 85;
        }
        v
    }

    /// cgBoard.getBoardKey: a hash of the squares only. NOTE: the engine's memo uses it, so two
    /// positions with the same squares share a score regardless of side to move or castling.
    pub fn key(&self) -> i32 {
        let mut h: i32 = 2166136261u32 as i32;
        for &s in &self.sq {
            h = (h ^ s as i32).wrapping_mul(16777619);
        }
        h = h.wrapping_add(h << 13);
        h ^= h >> 7;
        h = h.wrapping_add(h << 3);
        h ^= h >> 17;
        h = h.wrapping_add(h << 5);
        h
    }

    // ------------------------------------------------------------------ move generation
    /// removeIllegalMoves(moveSet), appended to `list`
    fn pseudo(&self, k: usize, from: usize, list: &mut Vec<FMove>, ep: u8, ep_on: u8) {
        let s = sets::sets();
        let (st, len, ty) = (s.start[k], s.len[k], s.ty[k]);
        let piece = self.sq[from];
        let mut can_capture = !(ty == 1 || ty == -1);
        let is_white = piece > 0;
        let (mut ray_open, mut can_move_to_empty) = (true, true);
        let king_or_knight = ty == 6 || ty == 3;
        for i in 0..len {
            if king_or_knight {
                ray_open = true;
            }
            let t = s.moves[st + i];
            if t == -1 {
                ray_open = true;
                continue;
            }
            if t == -2 {
                can_capture = true;
                can_move_to_empty = false;
                continue;
            }
            if can_move_to_empty && !ray_open {
                continue;
            }
            let target = self.sq[t as usize];
            if target == 0 && can_move_to_empty {
                list.push(FMove::new(from as u8, t as u8, s.pv[st + i]));
                continue;
            }
            let enemy = (piece <= 0 && target > 0) || (is_white && target < 0);
            if enemy {
                if !can_capture {
                    ray_open = false;
                    continue;
                }
                list.push(FMove::new(from as u8, t as u8, if target == 6 || target == -6 { 50 } else { 127 }));
                ray_open = false;
                continue;
            }
            if target == 0 && can_capture && !can_move_to_empty && ep == t as u8 {
                list.push(FMove::special(from as u8, t as u8, 127, 2, ep_on, 0));
                continue;
            }
            if (piece > 0 && target > 0) || (piece <= 0 && target < 0) {
                ray_open = false;
            }
        }
    }

    /// pseudo() for the other side, recording only destinations (all / non-pawn) and captures,
    /// which is all findLegalMoves uses of the enemy's moves.
    fn pseudo_enemy(&self, k: usize, from: usize, all: &mut u64, non_pawn: &mut u64, caps: &mut Vec<FMove>, ep: u8) {
        let s = sets::sets();
        let (st, len, ty) = (s.start[k], s.len[k], s.ty[k]);
        let piece = self.sq[from];
        let pawn = piece == 1 || piece == -1;
        let mut can_capture = !(ty == 1 || ty == -1);
        let is_white = piece > 0;
        let (mut ray_open, mut can_move_to_empty) = (true, true);
        let king_or_knight = ty == 6 || ty == 3;
        for i in 0..len {
            if king_or_knight {
                ray_open = true;
            }
            let t = s.moves[st + i];
            if t == -1 {
                ray_open = true;
                continue;
            }
            if t == -2 {
                can_capture = true;
                can_move_to_empty = false;
                continue;
            }
            if can_move_to_empty && !ray_open {
                continue;
            }
            let bit = 1u64 << t;
            let target = self.sq[t as usize];
            if target == 0 && can_move_to_empty {
                *all |= bit;
                if !pawn {
                    *non_pawn |= bit;
                }
                continue;
            }
            let enemy = (piece <= 0 && target > 0) || (is_white && target < 0);
            if enemy {
                if !can_capture {
                    ray_open = false;
                    continue;
                }
                *all |= bit;
                if !pawn {
                    *non_pawn |= bit;
                }
                caps.push(FMove::new(from as u8, t as u8, 0));
                ray_open = false;
                continue;
            }
            if target == 0 && can_capture && !can_move_to_empty && ep == t as u8 {
                *all |= bit;
                if !pawn {
                    *non_pawn |= bit;
                }
                continue;
            }
            if (piece > 0 && target > 0) || (piece <= 0 && target < 0) {
                ray_open = false;
            }
        }
    }

    /// cgBoard.findLegalMoves. The result comes from the pool; hand it back with give().
    fn find_legal(&mut self, as_white: bool, disregard: bool) -> Vec<FMove> {
        let mut legal = self.rent();
        let mut enemy = self.rent();
        let (mut e_all, mut e_non_pawn) = (0u64, 0u64);
        let (mut ep, mut ep_on) = (65u8, 65u8);
        if let Some(h) = self.hist.last() {
            let l = h.m;
            if l.to != 65 && self.sq[l.to as usize].abs() == 1 && (l.from as i32 - l.to as i32).abs() == 16 {
                ep = (l.from as i32 + if l.to < 32 { 16 } else { 0 } - 8) as u8;
                ep_on = l.to;
            }
        }
        for i in 0..64 {
            let p = self.sq[i];
            if (as_white && p > 0) || (!as_white && p < 0) {
                self.pseudo(key(p, i), i, &mut legal, ep, ep_on);
            } else if p != 0 {
                self.pseudo_enemy(key(p, i), i, &mut e_all, &mut e_non_pawn, &mut enemy, ep);
            }
        }

        let sq = &self.sq;
        let mut castle_side_white = as_white;
        let (mut can_short, mut can_long) = (false, false);
        if as_white {
            if self.wk == 0 && !self.w_castled && !(self.wh != 0 && self.wa != 0) {
                can_short = self.wh == 0 && sq[63] == 2;
                can_long = self.wa == 0 && sq[56] == 2;
                if can_short {
                    can_short = sq[61] == 0 && sq[62] == 0;
                }
                castle_side_white = true;
                if can_long {
                    can_long = sq[58] == 0 && sq[59] == 0 && sq[57] == 0;
                }
            }
        } else if self.bk == 0 && !self.b_castled && !(self.bh != 0 && self.ba != 0) {
            can_short = self.bh == 0 && sq[7] == -2;
            can_long = self.ba == 0 && sq[0] == -2;
            if can_short {
                can_short = sq[5] == 0 && sq[6] == 0;
            }
            castle_side_white = false;
            if can_long {
                can_long = sq[2] == 0 && sq[3] == 0 && sq[1] == 0;
            }
        }

        let mut not_in_check = true;
        let mut ray = std::mem::take(&mut self.ray);
        for em in &enemy {
            let (em_to, em_from) = (em.to, em.from);
            let victim = self.sq[em_to as usize];
            if victim == 6 || victim == -6 {
                can_long = false;
                can_short = false;
                not_in_check = false;
                continue;
            }
            if self.full_ray(key(self.sq[em_from as usize], em_from as usize), em_to, &mut ray) && !ray.is_empty() {
                let mut king_idx: isize = -1;
                let mut blockers = 0;
                let att = self.sq[em_from as usize];
                for (i, &r) in ray.iter().enumerate() {
                    let s = self.sq[r as usize];
                    if (s == 6 && att < 0) || (s == -6 && att > 0) {
                        king_idx = i as isize;
                        break;
                    }
                    if r != em_from && s != 0 {
                        blockers += 1;
                    }
                }
                if king_idx != -1 && blockers == 1 {
                    ray.truncate(king_idx as usize);
                    if !disregard {
                        // NOTE: a pinned piece may still move along the ray or capture the pinner
                        let mut j = legal.len();
                        while j != 0 {
                            j -= 1;
                            let lm = legal[j];
                            if ray.contains(&lm.from) && !ray.contains(&lm.to) && lm.to != em_from {
                                legal.remove(j);
                            }
                        }
                    }
                }
            }
        }
        self.ray = ray;

        // castling through squares any enemy move reaches
        if as_white {
            if e_all & (1 << 61 | 1 << 62) != 0 {
                can_short = false;
            }
            if e_all & (1 << 58 | 1 << 59) != 0 {
                can_long = false;
            }
        } else {
            if e_all & (1 << 5 | 1 << 6) != 0 {
                can_short = false;
            }
            if e_all & (1 << 2 | 1 << 3) != 0 {
                can_long = false;
            }
        }
        if castle_side_white && can_long {
            legal.push(FMove::special(60, 58, 30, 1, 56, 59));
        } else if can_long && !as_white {
            legal.push(FMove::special(4, 2, 30, 1, 0, 3));
        }
        if castle_side_white && can_short {
            legal.push(FMove::special(60, 62, 30, 1, 63, 61));
        } else if can_short && !as_white {
            legal.push(FMove::special(4, 6, 30, 1, 7, 5));
        }

        if legal.len() > 1 {
            dotnet_sort(&mut legal, &|x: &FMove, y: &FMove| (x.pv as i32).cmp(&(y.pv as i32)) as i32);
        }
        legal.reverse();

        if !disregard {
            let mut i = legal.len();
            while i > 0 {
                i -= 1;
                let lm = legal[i];
                if self.sq[lm.from as usize].abs() == 6 && lm.to < 64 && e_non_pawn & (1u64 << lm.to) != 0 {
                    legal.remove(i);
                }
            }
        }
        if legal.is_empty() {
            legal.push(NULL_MOVE);
        }
        if !not_in_check && !disregard {
            let mut checkers = 0;
            let mut first = FMove::default();
            for e in &enemy {
                let v = self.sq[e.to as usize];
                if v == 6 || v == -6 {
                    if checkers == 0 {
                        first = *e;
                    }
                    checkers += 1;
                }
            }
            if checkers == 1 {
                let mut block = std::mem::take(&mut self.block);
                self.block_squares(&first, &mut block);
                let mut i = legal.len();
                while i > 0 {
                    i -= 1;
                    let lm = legal[i];
                    if lm.to == 65 || block.contains(&lm.to) || self.sq[lm.from as usize].abs() == 6 {
                        continue;
                    }
                    legal.remove(i);
                }
                self.block = block;
            } else {
                let mut i = legal.len();
                while i > 0 {
                    i -= 1;
                    let lm = legal[i];
                    if lm.from > 64 || self.sq[lm.from as usize].abs() == 6 {
                        continue;
                    }
                    legal.remove(i);
                }
            }
        }
        if legal.is_empty() {
            legal.push(NULL_MOVE);
        }
        self.give(enemy);
        legal
    }

    /// _getFullRayFor(set, includes) into `ray`; false when the game's version returns null
    fn full_ray(&self, k: usize, includes: u8, ray: &mut Vec<u8>) -> bool {
        let s = sets::sets();
        let ty = s.ty[k];
        if !(ty == 2 || ty == 4 || ty == 5) {
            return false;
        }
        let (st, len) = (s.start[k], s.len[k]);
        let mv = &s.moves[st..st + len];
        let (mut ray_start, mut ray_end, mut found) = (0usize, 65usize, false);
        for (i, &m) in mv.iter().enumerate() {
            if !found && m == -1 {
                ray_start = i + 1;
            } else if found && m == -1 {
                ray_end = i;
                break;
            }
            if m as i32 == includes as i32 {
                found = true;
            }
        }
        if !found {
            return false;
        }
        ray.clear();
        for &m in &mv[ray_start..ray_end.min(len)] {
            if m >= 0 {
                ray.push(m as u8);
            }
        }
        true
    }

    /// _getBlockAttackSquares(checker) into `block`
    fn block_squares(&self, checker: &FMove, block: &mut Vec<u8>) {
        block.clear();
        block.push(checker.from);
        let t = self.sq[checker.from as usize].abs();
        if t == 2 || t == 4 || t == 5 {
            let s = sets::sets();
            let k = key(t, checker.from as usize);
            let (st, len) = (s.start[k], s.len[k]);
            let mv = &s.moves[st..st + len];
            let (mut ray_start, mut ray_end) = (0usize, 0usize);
            for (i, &m) in mv.iter().enumerate().take(256) {
                if m == -1 {
                    ray_start = i;
                } else if m as i32 == checker.to as i32 {
                    ray_end = i;
                    break;
                }
            }
            // NOTE: starts at the -1 separator, which becomes square 255 and never matches
            for &m in &mv[ray_start..ray_end.max(ray_start)] {
                block.push(m as u8);
            }
        }
    }

    fn verify_legality(&mut self, m: &FMove) -> bool {
        if m.to == 65 {
            return false;
        }
        if self.sq[m.from as usize].abs() == 6 {
            self.make(m);
            let replies = self.find_legal(self.white_turn, true);
            let ok = !replies.iter().any(|r| r.to == m.to);
            self.give(replies);
            self.revert();
            return ok;
        }
        true
    }

    fn find_strict(&mut self, as_white: bool) -> Vec<FMove> {
        let mut list = self.find_legal(as_white, false);
        let mut i = list.len();
        while i > 0 {
            i -= 1;
            let m = list[i];
            if !self.verify_legality(&m) {
                list.remove(i);
            }
        }
        list
    }

    /// The moves the game offers the side to move (strictly legal, no null move).
    pub fn legal_moves(&mut self) -> Vec<FMove> {
        let l = self.find_strict(self.white_turn);
        let r = l.clone();
        self.give(l);
        r
    }

    /// findLegalMoves as used inside the engine's search (for tests).
    pub fn pseudo_legal(&mut self, as_white: bool, disregard: bool) -> Vec<FMove> {
        let l = self.find_legal(as_white, disregard);
        let r = l.clone();
        self.give(l);
        r
    }

    /// cgBoard.isChecked
    pub fn in_check(&mut self, as_white: bool) -> bool {
        let l = self.find_legal(!as_white, false);
        let c = l.iter().any(|m| m.to != 65 && self.sq[m.to as usize].abs() == 6);
        self.give(l);
        c
    }

    // ------------------------------------------------------------------ cgEngine
    fn alfa_beta(&mut self, node: &FMove, mut depth: u8, mut alpha: i32, mut beta: i32, maximizing: bool) -> i32 {
        self.make(node);
        let k = self.key();
        let value;
        if let Some(c) = self.memo.get(k) {
            value = c;
        } else if depth == 0 {
            value = self.evaluate();
        } else {
            let mut v;
            if maximizing {
                // NOTE: white checks king safety strictly only once 16 or fewer pieces are left
                let ms = if self.remaining > 16 { self.find_legal(true, false) } else { self.find_strict(true) };
                depth -= 1;
                v = i32::MIN;
                for m in &ms {
                    v = v.max(self.alfa_beta(m, depth, alpha, beta, false));
                    alpha = alpha.max(v);
                    if alpha > beta {
                        break;
                    }
                }
                self.give(ms);
            } else {
                let ms = self.find_legal(false, false);
                depth -= 1;
                v = i32::MAX;
                for m in &ms {
                    v = v.min(self.alfa_beta(m, depth, alpha, beta, true));
                    beta = beta.min(v);
                    if beta < alpha {
                        break;
                    }
                }
                self.give(ms);
            }
            value = v;
            self.memo.try_add(k, value);
        }
        self.revert();
        value
    }

    /// cgEngine.MakeMove's root list after both of the game's sorts, each move with its value.
    /// The first element is the move the game plays. Searched on a copy without history, like
    /// the game (so the engine never plays en passant at the root).
    pub fn engine_root(&self, weak: u8, strong: u8, end: u8) -> Vec<FMove> {
        let mut b = self.clone_with(false);
        let white = b.white_turn;
        let mut root = b.find_strict(white);
        if root.is_empty() {
            return root;
        }
        b.memo = IntMap::new();
        let n = root.len();
        for i in (0..n).rev() {
            let depth = if n <= 9 {
                end
            } else if root[i].pv > 20 {
                strong
            } else {
                weak
            };
            let m = root[i];
            root[i].val = b.alfa_beta(&m, depth, i32::MIN, i32::MAX, !white);
        }
        // _processThreadded sorts, then _analysisComplete sorts again
        for _ in 0..2 {
            if root.len() > 1 {
                dotnet_sort(&mut root, &|x: &FMove, y: &FMove| x.val.cmp(&y.val) as i32);
            }
            if white {
                root.reverse();
            }
        }
        root
    }

    /// The move the game's engine plays here, or None if it has no move.
    pub fn engine_move(&self, weak: u8, strong: u8, end: u8) -> Option<FMove> {
        self.engine_root(weak, strong, end).first().copied()
    }

    // ------------------------------------------------------------------ text form
    /// `<64 squares> <w|b> <wc> <bc> <wa> <wh> <ba> <bh> <wk> <bk> <remaining> <lastFrom> <lastTo>`,
    /// squares as "kqbnrp.PRNBQK"; lastFrom/lastTo are 255 when there is no previous move.
    pub fn to_state(&self) -> String {
        let (lf, lt) = self.last().map(|m| (m.from, m.to)).unwrap_or((NONE, NONE));
        format!(
            "{} {} {} {} {} {} {} {} {} {} {} {} {}",
            self.squares_string(),
            if self.white_turn { "w" } else { "b" },
            self.w_castled as u8,
            self.b_castled as u8,
            self.wa,
            self.wh,
            self.ba,
            self.bh,
            self.wk,
            self.bk,
            self.remaining,
            lf,
            lt
        )
    }

    pub fn squares_string(&self) -> String {
        const P: &[u8] = b"kqbnrp.PRNBQK";
        self.sq.iter().map(|&s| P[(s + 6) as usize] as char).collect()
    }

    /// Parse to_state() output. Also accepts just the squares (64 chars, '/' allowed), meaning a
    /// new game with white to move.
    pub fn from_state(s: &str) -> Result<FastBoard, String> {
        let mut parts = s.split_whitespace();
        let squares: String = parts.next().ok_or("empty state")?.chars().filter(|&c| c != '/').collect();
        if squares.len() != 64 {
            return Err(format!("expected 64 squares, got {}", squares.len()));
        }
        let mut sq = [0i8; 64];
        for (i, c) in squares.chars().enumerate() {
            sq[i] = match "kqbnrp.PRNBQK".find(c) {
                Some(p) => p as i8 - 6,
                None => return Err(format!("bad square '{c}'")),
            };
        }
        let mut f = FastBoard::from_squares(sq);
        let rest: Vec<&str> = parts.collect();
        if rest.is_empty() {
            return Ok(f);
        }
        if rest.len() != 12 {
            return Err(format!("expected 13 fields, got {}", rest.len() + 1));
        }
        let n = |i: usize| rest[i].parse::<u8>().map_err(|e| format!("field {}: {e}", i + 2));
        f.white_turn = match rest[0] {
            "w" => true,
            "b" => false,
            x => return Err(format!("bad side '{x}'")),
        };
        f.w_castled = n(1)? != 0;
        f.b_castled = n(2)? != 0;
        f.wa = n(3)?;
        f.wh = n(4)?;
        f.ba = n(5)?;
        f.bh = n(6)?;
        f.wk = n(7)?;
        f.bk = n(8)?;
        f.remaining = n(9)?;
        let (lf, lt) = (n(10)?, n(11)?);
        if lf != NONE && lt != NONE {
            f.set_last(lf, lt);
        }
        Ok(f)
    }
}

impl fmt::Display for FastBoard {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        let s = self.squares_string();
        for r in 0..8 {
            if r > 0 {
                f.write_str("/")?;
            }
            f.write_str(&s[r * 8..r * 8 + 8])?;
        }
        Ok(())
    }
}

/// i32 -> i32 open-addressing map with Dictionary's TryGetValue / TryAdd semantics.
struct IntMap {
    keys: Vec<i32>,
    vals: Vec<i32>,
    used: Vec<bool>,
    count: usize,
    mask: usize,
}

impl IntMap {
    fn empty() -> Self {
        IntMap { keys: vec![], vals: vec![], used: vec![], count: 0, mask: 0 }
    }
    fn new() -> Self {
        let n = 1 << 14;
        IntMap { keys: vec![0; n], vals: vec![0; n], used: vec![false; n], count: 0, mask: n - 1 }
    }
    #[inline]
    fn slot(k: i32, mask: usize) -> usize {
        ((k.wrapping_mul(-1640531535) as u32) >> 7) as usize & mask
    }
    #[inline]
    fn get(&self, k: i32) -> Option<i32> {
        if self.used.is_empty() {
            return None;
        }
        let mut i = Self::slot(k, self.mask);
        while self.used[i] {
            if self.keys[i] == k {
                return Some(self.vals[i]);
            }
            i = (i + 1) & self.mask;
        }
        None
    }
    fn try_add(&mut self, k: i32, v: i32) {
        if self.used.is_empty() {
            *self = IntMap::new();
        }
        let mut i = Self::slot(k, self.mask);
        while self.used[i] {
            if self.keys[i] == k {
                return;
            }
            i = (i + 1) & self.mask;
        }
        self.used[i] = true;
        self.keys[i] = k;
        self.vals[i] = v;
        self.count += 1;
        if self.count * 2 > self.keys.len() {
            self.grow();
        }
    }
    fn grow(&mut self) {
        let n = self.keys.len() * 2;
        let old = std::mem::replace(self, IntMap { keys: vec![0; n], vals: vec![0; n], used: vec![false; n], count: 0, mask: n - 1 });
        self.count = old.count;
        for j in 0..old.keys.len() {
            if old.used[j] {
                let mut i = Self::slot(old.keys[j], self.mask);
                while self.used[i] {
                    i = (i + 1) & self.mask;
                }
                self.used[i] = true;
                self.keys[i] = old.keys[j];
                self.vals[i] = old.vals[j];
            }
        }
    }
}
