//! Really Bad Chess: a Rust port of the model of the game's chess engine and of the planner that
//! beats it (the C# in ../src and ../exploit).
//!
//! * [`board::FastBoard`]: the game's board, move generator and engine. `engine_move` returns
//!   exactly the move the game's AI plays (checked against vectors from the C# port in tests/).
//! * [`gen`]: the game's army generator, seed for seed.
//! * [`planner::Planner`]: searches our moves against the exact engine for the shortest win.
//! * `wasm` feature: a wasm-bindgen API (src/wasm.rs).

pub mod board;
pub mod gen;
pub mod planner;
pub mod sets;
pub mod sort;
#[cfg(feature = "wasm")]
pub mod wasm;

pub use board::{FMove, FastBoard};
pub use planner::{Choice, Planner};

/// Moves as "from-to from-to ..." (square indices), the format of the test vectors and the
/// wasm API.
pub fn moves_to_string(ms: &[FMove]) -> String {
    ms.iter().map(|m| format!("{}-{}", m.from, m.to)).collect::<Vec<_>>().join(" ")
}
