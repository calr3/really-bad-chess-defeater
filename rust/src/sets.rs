//! Precomputed move sets: a port of cgMoveGenerator and cgMoveSet (src/cgMoveGen.cs), laid out
//! flat like FSets in exploit/FastModel.cs.
//!
//! Each set is a list of target squares with separators: -1 ends a ray, -2 (pawns only) switches
//! from pushes to captures. Every target carries a positional value, used to order moves.

use std::sync::OnceLock;

pub const POS_BISHOP: [i8; 64] = [
    -20, 0, 0, 0, 0, 0, 0, -20, //
    0, 22, 5, 5, 5, 5, 22, 0, //
    10, 0, 12, 15, 15, 12, 5, 10, //
    5, 10, 12, 25, 25, 12, 10, 5, //
    5, 10, 12, 25, 25, 12, 10, 5, //
    10, 5, 12, 12, 15, 12, 5, 10, //
    0, 22, 5, 5, 5, 5, 22, 0, //
    -20, 0, 0, -10, 0, -10, 0, -20,
];
pub const POS_ROOK: [i8; 64] = [
    0, 5, 15, 18, 18, 15, 5, 0, //
    0, 0, 0, 0, 0, 0, 0, 0, //
    0, 0, 0, 0, 0, 0, 0, 0, //
    0, 0, 0, 0, 0, 0, 0, 0, //
    0, 0, 0, 0, 0, 0, 0, 0, //
    0, 0, 0, 0, 0, 0, 0, 0, //
    0, 0, 0, 0, 0, 0, 0, 0, //
    0, 5, 7, 18, 18, 7, 5, 0,
];
pub const POS_KNIGHT: [i8; 64] = [
    0, -5, 0, 0, 0, 0, -5, 0, //
    0, 0, 0, 5, 5, 0, 0, 0, //
    0, 5, 25, 15, 15, 25, 5, 0, //
    0, 5, 15, 30, 30, 15, 5, 0, //
    0, 5, 15, 30, 30, 15, 5, 0, //
    0, 5, 15, 15, 15, 15, 5, 0, //
    0, 0, 0, 5, 5, 0, 0, 0, //
    0, -5, 0, 0, 0, 0, -5, 0,
];
pub const POS_PAWN: [i8; 64] = [
    0, 0, 0, 0, 0, 0, 0, 0, //
    0, 0, 0, 0, 0, 0, 0, 0, //
    3, 3, 5, 5, 5, 5, 3, 3, //
    0, 3, 12, 15, 23, 12, 5, 0, //
    0, 0, 15, 15, 22, 12, 7, 0, //
    5, 5, 15, 15, 15, 15, 5, 5, //
    0, 0, 0, 0, 0, 0, 0, 0, //
    0, 0, 0, 0, 0, 0, 0, 0,
];

pub struct Sets {
    pub start: Vec<usize>,
    pub len: Vec<usize>,
    pub ty: Vec<i8>,
    pub moves: Vec<i8>,
    pub pv: Vec<i8>,
}

/// cgBoard._findMoveSetFor(piece, idx): black non-pawns use the white set.
#[inline]
pub fn key(piece: i8, idx: usize) -> usize {
    let p = if piece < -1 { -piece } else { piece };
    (p as i32 + 1) as usize * 64 + idx
}

pub fn sets() -> &'static Sets {
    static S: OnceLock<Sets> = OnceLock::new();
    S.get_or_init(build)
}

fn build() -> Sets {
    let n = 8 * 64;
    let mut s = Sets { start: vec![0; n], len: vec![0; n], ty: vec![0; n], moves: vec![], pv: vec![] };
    for t in [-1i32, 1, 2, 3, 4, 5, 6] {
        for i in 0..64i32 {
            let list = match t {
                5 => find_moves(&[-9, -8, -7, -1, 1, 7, 8, 9], i),
                2 => find_moves(&[-8, -1, 1, 8], i),
                4 => find_moves(&[-9, -7, 7, 9], i),
                3 => knight(i),
                6 => king(i),
                1 => pawn(i, true),
                _ => pawn(i, false),
            };
            let k = ((t + 1) * 64 + i) as usize;
            s.start[k] = s.moves.len();
            s.len[k] = list.len();
            s.ty[k] = t as i8;
            for &m in &list {
                s.moves.push(m as i8);
                s.pv.push(positional_value(t, i, m as i8));
            }
        }
    }
    s
}

/// cgMoveSet._findPositionalValueAt
fn positional_value(ty: i32, from: i32, at: i8) -> i8 {
    if at < 0 {
        return 0;
    }
    let (a, f) = (at as usize, from as usize);
    match ty {
        -1 | 1 => POS_PAWN[a].wrapping_sub(POS_PAWN[f]),
        2 => POS_ROOK[a].wrapping_sub(POS_ROOK[f]),
        3 => POS_KNIGHT[a].wrapping_sub(POS_KNIGHT[f]),
        4 => POS_BISHOP[a].wrapping_sub(POS_BISHOP[f]),
        // the game's queen uses the bishop table at index 0, not at the destination
        5 => POS_BISHOP[0].wrapping_sub(POS_BISHOP[f]),
        _ => 0,
    }
}

fn left_border(p: i32) -> bool {
    (0..64).contains(&p) && p % 8 == 0
}
fn right_border(p: i32) -> bool {
    (0..64).contains(&p) && p % 8 == 7
}
fn left_dir(d: i32) -> bool {
    matches!(d, -9 | -1 | 7)
}
fn right_dir(d: i32) -> bool {
    matches!(d, -7 | 1 | 9)
}
fn blocked(p: i32, d: i32) -> bool {
    (left_border(p) && left_dir(d)) || (right_border(p) && right_dir(d))
}

fn find_moves(dirs: &[i32], idx: i32) -> Vec<i32> {
    let mut moves = vec![];
    for &dir in dirs {
        let mut pos = idx;
        let b = blocked(pos, dir);
        pos += dir;
        if !b && (pos as u32) <= 63 {
            loop {
                moves.push(pos);
                if blocked(pos, dir) {
                    break;
                }
                pos += dir;
                if (pos as u32) >= 64 {
                    break;
                }
            }
        }
        moves.push(-1);
    }
    moves
}

fn king(idx: i32) -> Vec<i32> {
    let mut moves = vec![];
    for dir in [-9, -8, -7, -1, 1, 7, 8, 9] {
        let t = idx + dir;
        if (0..=63).contains(&t) && !blocked(idx, dir) {
            moves.push(t);
        }
    }
    moves
}

fn knight(idx: i32) -> Vec<i32> {
    let paths: [[i32; 3]; 8] =
        [[-8, -8, -1], [-8, -8, 1], [-1, -1, -8], [-1, -1, 8], [8, 8, 1], [8, 8, -1], [1, 1, -8], [1, 1, 8]];
    let mut moves = vec![];
    for path in paths {
        if blocked(idx, path[0]) || (idx as u32) > 63 {
            continue;
        }
        let mut cur = idx;
        for i in 0..=path.len() {
            if i == path.len() {
                moves.push(cur);
                break;
            }
            let b = blocked(cur, path[i]);
            cur += path[i];
            if b || (cur as u32) > 63 {
                break;
            }
        }
    }
    moves
}

fn pawn(idx: i32, white: bool) -> Vec<i32> {
    let mut moves = vec![];
    let fwd = if white { -8 } else { 8 };
    let t = idx + fwd;
    if (t as u32) <= 63 && !blocked(idx, fwd) {
        moves.push(t);
    }
    // both back ranks count as pawn rows for the double step
    if white && idx >= 48 {
        moves.push(idx - 16);
        moves.push(-1);
    } else if !white && idx <= 15 {
        moves.push(idx + 16);
        moves.push(-1);
    }
    moves.push(-2);
    let atk: [i32; 2] = if white { [-7, -9] } else { [7, 9] };
    for dir in atk {
        let t = idx + dir;
        if (t as u32) <= 63 && !blocked(idx, dir) {
            moves.push(t);
        }
    }
    moves
}
