//! wasm-bindgen API (build with `--features wasm`, see README). Positions are passed as state
//! strings (FastBoard::to_state), moves as "from-to" square indices, 0 = a8 ... 63 = h1.
//!
//! wasm32 has no threads here, so the planner runs single-threaded and blocks while it thinks:
//! run it in a Web Worker in a browser.

use crate::board::FastBoard;
use crate::gen::{random_board, Mode};
use crate::planner::Planner;
use wasm_bindgen::prelude::*;

fn parse(state: &str) -> Result<FastBoard, JsError> {
    FastBoard::from_state(state).map_err(|e| JsError::new(&e))
}

/// Starting position of a ranked game at `rank` (the game's generator, same seed same board).
#[wasm_bindgen(js_name = rankedBoard)]
pub fn ranked_board(seed: i32, rank: i32) -> String {
    random_board(seed, Mode::Ranked(rank)).board.to_state()
}

/// Starting position of a free-play game at this difficulty.
#[wasm_bindgen(js_name = freeplayBoard)]
pub fn freeplay_board(seed: i32, difficulty: i32) -> String {
    random_board(seed, Mode::Freeplay(difficulty)).board.to_state()
}

/// The daily game; `date` is yyyyMMdd as a number, e.g. 20261115.
#[wasm_bindgen(js_name = dailyBoard)]
pub fn daily_board(date: i32) -> String {
    random_board(date, Mode::Daily).board.to_state()
}

/// The weekly game for the game's weekly seed.
#[wasm_bindgen(js_name = weeklyBoard)]
pub fn weekly_board(seed: i32) -> String {
    random_board(seed, Mode::Weekly).board.to_state()
}

/// Legal moves of the side to move, "from-to from-to ...". Empty: the side to move has lost.
#[wasm_bindgen(js_name = legalMoves)]
pub fn legal_moves(state: &str) -> Result<String, JsError> {
    Ok(crate::moves_to_string(&parse(state)?.legal_moves()))
}

/// Play a legal move and return the new state.
#[wasm_bindgen]
pub fn play(state: &str, from: u8, to: u8) -> Result<String, JsError> {
    let mut b = parse(state)?;
    let m = b.legal_moves().into_iter().find(|m| m.from == from && m.to == to).ok_or_else(|| JsError::new("illegal move"))?;
    b.make(&m);
    Ok(b.to_state())
}

/// The move the game's engine plays here, "from-to", or "" if it has none. The game uses 3, 5, 5.
#[wasm_bindgen(js_name = engineMove)]
pub fn engine_move(state: &str, weak: u8, strong: u8, end: u8) -> Result<String, JsError> {
    Ok(parse(state)?.engine_move(weak, strong, end).map(|m| format!("{}-{}", m.from, m.to)).unwrap_or_default())
}

/// Algebraic name of a square index, e.g. 52 -> "e2".
#[wasm_bindgen(js_name = squareName)]
pub fn square_name(i: u8) -> String {
    crate::board::square_name(i)
}

/// The planner. Keep one per game: it remembers its plan between moves.
#[wasm_bindgen(js_name = Planner)]
pub struct WasmPlanner(Planner);

#[wasm_bindgen(js_class = Planner)]
impl WasmPlanner {
    /// `seconds`: thinking time per move.
    #[wasm_bindgen(constructor)]
    pub fn new(seconds: f64) -> WasmPlanner {
        let mut p = Planner::new();
        p.seconds_per_move = seconds;
        p.threads = 1;
        WasmPlanner(p)
    }

    /// Also stop after this many searched positions per move (0: no limit).
    #[wasm_bindgen(js_name = setMaxNodes)]
    pub fn set_max_nodes(&mut self, n: u32) {
        self.0.max_nodes = if n == 0 { None } else { Some(n as usize) };
    }

    /// Our move in `state` (our turn): "from-to", plus " win N" if a forced win in N moves was
    /// found. "" if we have no move.
    pub fn choose(&mut self, state: &str) -> Result<String, JsError> {
        let b = parse(state)?;
        Ok(match self.0.choose(&b) {
            Some(c) => {
                let mut s = format!("{}-{}", c.mv.from, c.mv.to);
                if let Some(n) = c.win_in {
                    s += &format!(" win {n}");
                }
                s
            }
            None => String::new(),
        })
    }

    /// Tell the planner what was played: our move and the opponent's reply.
    pub fn observe(&mut self, our_from: u8, our_to: u8, reply_from: u8, reply_to: u8) {
        use crate::board::FMove;
        self.0.observe(FMove::new(our_from, our_to, 0), FMove::new(reply_from, reply_to, 0));
    }

    /// Statistics of the last choose().
    #[wasm_bindgen(js_name = lastInfo)]
    pub fn last_info(&self) -> String {
        self.0.last_info.clone()
    }
}
