//! The game's army generator: cgChessBoardScript.getRandomBoard (src/cgChessBoardScript.cs) with
//! UnityEngine.Random (xorshift128, ported from libunity.so). Same seed, same board.

use crate::board::FastBoard;

/// UnityEngine.Random. All arithmetic is f32 like the game's (Rust never fuses it into FMA).
pub struct UnityRandom {
    s: [u32; 4],
}

impl UnityRandom {
    pub fn new(seed: i32) -> Self {
        let s0 = seed as u32;
        let s1 = s0.wrapping_mul(1812433253).wrapping_add(1);
        let s2 = s1.wrapping_mul(1812433253).wrapping_add(1);
        let s3 = s2.wrapping_mul(1812433253).wrapping_add(1);
        UnityRandom { s: [s0, s1, s2, s3] }
    }

    fn next(&mut self) -> u32 {
        let t = self.s[0] ^ (self.s[0] << 11);
        self.s[0] = self.s[1];
        self.s[1] = self.s[2];
        self.s[2] = self.s[3];
        self.s[3] = self.s[3] ^ (self.s[3] >> 19) ^ t ^ (t >> 8);
        self.s[3]
    }

    /// Random.value, in [0, 1] inclusive
    pub fn value(&mut self) -> f32 {
        (self.next() & 0x7FFFFF) as f32 * (1.0f32 / 8388607.0f32)
    }

    /// Random.Range(float, float). NOTE: Unity computes min*t + (1-t)*max.
    pub fn range_f(&mut self, min: f32, max: f32) -> f32 {
        let t = self.value();
        min * t + (1.0f32 - t) * max
    }

    /// Random.Range(int, int): max exclusive
    pub fn range_i(&mut self, min: i32, max: i32) -> i32 {
        if min < max {
            (min as u32).wrapping_add(self.next() % (max.wrapping_sub(min) as u32)) as i32
        } else if max < min {
            (min as u32).wrapping_sub(self.next() % (min.wrapping_sub(max) as u32)) as i32
        } else {
            min
        }
    }
}

/// cgChessBoardScript.INT_TO_RANK: f32 multiply, then round half to even
pub fn int_to_rank(x: i32) -> i32 {
    let f = (x + 30) as f32 * 1.66f32;
    (f as f64).round_ties_even() as i32
}

fn floor_to_int(f: f32) -> i32 {
    (f as f64).floor() as i32
}

fn point_value(p: i32) -> i32 {
    match p {
        1 => 1,
        2 => 5,
        3 | 4 => 3,
        5 => 9,
        _ => 0,
    }
}

/// Which screen the game was started from. They differ in how the target is chosen.
#[derive(Clone, Copy, Debug)]
pub enum Mode {
    /// Ranked play at this rank. Below rank 42 White's army is also weakened.
    Ranked(i32),
    /// Free play (and two-player "versus") at this slider difficulty.
    Freeplay(i32),
    /// Daily puzzle: the seed is the date as yyyyMMdd, the target is drawn from it.
    Daily,
    /// Weekly game: the seed comes from the week, the target is drawn from it.
    Weekly,
}

/// Generated starting position, with what the game records about it.
pub struct Generated {
    pub board: FastBoard,
    pub target: i32,
    pub spread: i32,
    pub prox: i32,
}

/// cgChessBoardScript.RankedBoard / getRandomBoard for a given seed.
pub fn random_board(seed: i32, mode: Mode) -> Generated {
    let mut rng = UnityRandom::new(seed);
    let (target, full_strength) = match mode {
        // The game passes the target and saves it as the player's rank, clamped at 0. NOTE: -1
        // means "use the saved rank", so -1 gives 0 while other negative ranks are used as given.
        Mode::Ranked(rank) => {
            let t = if rank == -1 { rank.max(int_to_rank(-30)) } else { rank };
            (t, t >= int_to_rank(-5))
        }
        Mode::Freeplay(d) => (d, true),
        Mode::Weekly => {
            let t = rng.range_i(int_to_rank(0), int_to_rank(12));
            rng = UnityRandom::new(seed);
            (t, true)
        }
        Mode::Daily => {
            let t = rng.range_i(int_to_rank(-9), int_to_rank(-1));
            rng = UnityRandom::new(seed);
            (t, true)
        }
    };

    let mut template: Vec<i8> = vec![-9, -9, -9, -9, -6, -9, -9, -9, -7, -7, -7, -8, -8, -8, -7, -7];
    template.extend(std::iter::repeat_n(0, 32));
    template.extend([7, 7, 7, 8, 8, 8, 7, 7, 9, 9, 9, 9, 6, 9, 9, 9]);

    let t = target as f32;
    let white_up;
    let (mut white_down, mut black_up, mut black_down) = (0f32, 0f32, 0f32);
    let mut tmp_down = 0f32;
    if !full_strength {
        black_down = t / -100f32;
        tmp_down = t / 100f32;
    }
    let mut tmp2 = 0f32;
    if target != 0 {
        black_up = t / 50f32;
        tmp2 = tmp_down;
    }
    if target < 0 {
        white_up = t / -50f32;
        black_up = 0.0;
    } else {
        white_up = 0.0;
        black_down = 0.0;
        white_down = tmp2;
    }
    let count_black_pawns = target < 0 && full_strength;
    let count_white_pawns = target > 0 && full_strength;

    let mut board: Vec<i8> = Vec::with_capacity(64);
    let (mut attempts, mut pawn_count, mut spread, mut black_value) = (0i32, 0i32, 0i32, 0i32);
    let mut prox;
    loop {
        prox = spread - target;
        if (prox as u32) <= 3 && !(pawn_count > 4 && attempts < 10000) && black_value != 0 {
            break;
        }
        attempts += 1;
        board.clear();
        let mut white_value = 0;
        black_value = 0;
        pawn_count = 0;
        for &sq in &template {
            let white = sq > 0;
            let (up, down) = if white { (white_up, white_down) } else { (black_up, black_down) };
            let p: i32 = match sq.abs() {
                7 => {
                    let mut p = rng.range_i(1, 4);
                    if target > 39 {
                        if p == 2 && (rng.value() as f64) > 0.1 {
                            p = floor_to_int(rng.range_f(3.0, 5.5)).min(4);
                        }
                    } else {
                        // NOTE: the first draw is discarded
                        p = rng.range_i(1, 3);
                        if p == 2 {
                            p = floor_to_int(rng.range_f(3.0, 5.5)).min(4);
                        }
                    }
                    pawn_rank_tail(&mut rng, p, up, down)
                }
                8 => {
                    let mut p = rng.range_i(1, 3);
                    if p == 2 {
                        p = floor_to_int(rng.range_f(3.0, 5.5)).min(4);
                    }
                    pawn_rank_tail(&mut rng, p, up, down)
                }
                9 => {
                    let mut p = rng.range_i(1, 6);
                    if down > rng.value() {
                        p = match p {
                            5 => 2,
                            2 => rng.range_i(3, 5),
                            _ => 1,
                        };
                    }
                    if up > rng.value() {
                        p = match p {
                            3 | 4 => 2,
                            2 => 5,
                            1 => rng.range_i(3, 5),
                            x => x,
                        };
                    }
                    p
                }
                _ => {
                    board.push(sq);
                    continue;
                }
            };
            if white {
                white_value += point_value(p);
                board.push(p as i8);
                if p == 1 && count_white_pawns {
                    pawn_count += 1;
                }
            } else {
                black_value += point_value(p);
                board.push(-p as i8);
                if p == 1 && count_black_pawns {
                    pawn_count += 1;
                }
            }
        }
        spread = int_to_rank(black_value - white_value);
    }
    let mut sq = [0i8; 64];
    sq.copy_from_slice(&board);
    Generated { board: FastBoard::from_squares(sq), target, spread, prox }
}

fn pawn_rank_tail(rng: &mut UnityRandom, mut p: i32, up: f32, down: f32) -> i32 {
    if down > rng.value() {
        p = 1;
    }
    let r = rng.value();
    if p == 1 && up > r {
        p = floor_to_int(rng.range_f(3.0, 5.5));
        if p >= 5 {
            p = 4;
        }
    }
    p
}

/// The board a ranked game at `rank` starts from with this seed.
pub fn ranked_board(seed: i32, rank: i32) -> FastBoard {
    random_board(seed, Mode::Ranked(rank)).board
}
