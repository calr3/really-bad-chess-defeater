# rbc: the Rust / WebAssembly port

A Rust port of the engine model, the army generator and the planner from the C# in `../src`
and `../exploit`. It compiles to native code and to WebAssembly (134 KB).

| module | port of | what it is |
|---|---|---|
| `src/board.rs` | `exploit/FastModel.cs` | the game's board, move generator and engine (`engine_move`) |
| `src/sets.rs` | `cgMoveGenerator`, `cgMoveSet` | precomputed move lists and positional values |
| `src/sort.rs` | .NET `ArraySortHelper<T>` | the unstable introsort the game sorts moves with |
| `src/gen.rs` | `getRandomBoard`, `UnityEngine.Random` | the army generator: same seed, same board |
| `src/planner.rs` | `exploit/Planner.cs` | the planner that searches for the shortest win |
| `src/wasm.rs` | — | wasm-bindgen API (feature `wasm`) |

## How it is checked

The engine's choices depend on details that a "normal" chess port would not keep. Examples:
the game's quirky move legality, its memo keyed by a hash of the squares only, and the order
.NET's unstable sort leaves equally valued moves in. So the port is checked against reference
outputs written by the C# model (`harness vectors`), which was itself checked move for move
against the faithful decompilation. The vectors are in `tests/data`:

| file | contents | checked |
|---|---|---|
| `pos.txt` | 1,500 positions from random and engine-guided playouts of generated boards | hash key, evaluation, check, strict and pseudo-legal move lists for both sides, and the engine's full root list after both sorts (every move's value and order) at 3/5/5 or 2/3/3 |
| `gen.txt` | 864 boards: ranked at ranks −5 to 120, free play, daily and weekly | identical boards |
| `sort.txt`, `sort-extra.txt` | 3,009 arrays, including 9 that reach the heapsort fallback | identical order of equal keys |
| `rng.txt` | `Random.value`, `Range(float)` and `Range(int)` sequences | identical bits |

`cargo test --release` runs all of it, in about 30 s. `js/test.cjs` runs a subset against
the wasm build. To regenerate the vectors (needs the .NET SDK):

```sh
dotnet run -c Release --project ../harness -- vectors tests/data 1500 1
```

## Native

```sh
cargo build --release
./target/release/rbc-cli board 5000 0              # ranked starting position (state string)
./target/release/rbc-cli engine <state>            # the game's move in a state
./target/release/rbc-cli choose 60 <state>         # the planner's move, 60 s budget
./target/release/rbc-cli plan 0,30,60 2 1000 120   # planner vs engine, like `harness plan`
```

A state string is `<64 squares> <w|b> <wc> <bc> <wa> <wh> <ba> <bh> <wk> <bk> <remaining>
<lastFrom> <lastTo>`. Squares run a8 … h1 as `kqbnrp.PRNBQK`. Then come side to move, castled
flags, rook and king move counters, the number of pieces, and the previous move (255 255 for
none; it enables en passant). Square indices are 0 = a8 … 63 = h1.

## WebAssembly

```sh
rustup target add wasm32-unknown-unknown
cargo install wasm-bindgen-cli --version 0.2.130   # must match Cargo.lock
cargo build --release --target wasm32-unknown-unknown --features wasm --lib
wasm-bindgen --target web --out-dir web/pkg target/wasm32-unknown-unknown/release/rbc.wasm
wasm-bindgen --target nodejs --out-dir pkg-node target/wasm32-unknown-unknown/release/rbc.wasm
node js/test.cjs pkg-node                          # wasm checks against the vectors
cd web && python3 -m http.server                   # web UI: open http://localhost:8000
```

API (all positions are state strings, moves are `"from-to"`):

```js
import init, * as rbc from './pkg/rbc.js';
await init();
let s = rbc.rankedBoard(5000, 0);       // also freeplayBoard(seed, difficulty), dailyBoard(yyyyMMdd), weeklyBoard(seed)
rbc.legalMoves(s);                      // "52-36 51-35 ..."; "" means the side to move has lost
rbc.engineMove(s, 3, 5, 5);             // the game's reply ("" if none); the game uses 3, 5, 5
s = rbc.play(s, 52, 36);                // apply a legal move
const p = new rbc.Planner(30);          // 30 s per move; keep one per game
p.choose(s);                            // "53-45 win 6": our move, and a forced win if found
p.observe(53, 45, 12, 28);              // after each pair of moves: ours, then the reply
p.planLine();                           // the win being followed, "f-t f-t ..." (ours, reply, ...)
p.setPlanLine(line);                    // restore it, e.g. into a new Planner
```

The web UI (`web/`) is published to GitHub Pages at
https://calr3.github.io/really-bad-chess-defeater/ by `.github/workflows/pages.yml` on every push
to `main` or `rust-port`. It is for playing along with a real game: set up the board the game shows (or
load a ranked board by seed), and it suggests each White move, predicts the engine's reply, and
lets you enter what the game actually played by tapping squares. It can also auto-play both sides.
It can read the board from a screenshot of the game (`web/vision.js`: piece silhouettes matched
against templates cut from real screenshots), both to set up a game and, mid-game, to work out
the move you played and the engine's reply.

The wasm build is single-threaded and blocks while it thinks, so in a browser run it in a Web
Worker, as `web/worker.js` does. `Planner.setMaxNodes(n)` also caps the search by positions
instead of time, which makes runs reproducible.

## Speed

Engine move (`engine_root`), single-threaded, the same 300 vector positions, one 4-core machine:

| | ms per move |
|---|---|
| C# (.NET 8) `FastBoard.EngineRoot` | 79 |
| Rust native | 66 |
| Rust wasm in Node 22 | 86 |

The native planner uses all cores, like the C# one. The wasm planner uses one, so in the same
time it searches about a quarter as many positions.
