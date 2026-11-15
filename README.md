# Really Bad Chess — engine decompilation and an engine that beats it

This directory holds a reverse-engineered C# port of the chess AI of *Really Bad Chess* for
Android, version 1.4.18 (`com.noodlecake.reallybadchess`). It also has an engine that wins
against that AI by exploiting its exact behaviour.

The APK is not included. To regenerate the analysis outputs, supply your own copy as
`baseapk.apk`:

```
sha256: 9afd1ebee52e04de9cde45815280da4fe269dffc7f4d172773a93685d4b508d3
```

## How the game is built

The game is Unity with an **IL2CPP** backend: the original C# was compiled to native code
(`lib/*/libil2cpp.so`), and its type, method and field names are in
`assets/bin/Data/Managed/Metadata/global-metadata.dat` (metadata version 31). The chess code is a
set of classes prefixed `cg` (`cgEngine`, `cgBoard`, `cgMoveGenerator`, `cgMoveSet`,
`cgSimpleMove`, …) plus the game layer in `cgChessBoardScript`.

`tools/` contains a small self-contained toolchain, written for this project, that recovers the
declarations and an annotated disassembly from the x86_64 build:

| file | what it does |
|---|---|
| `meta.py` | parser for `global-metadata.dat` v31 (types, methods, fields, defaults, literals) |
| `binary.py` | ELF reader: applies relocations, finds `Il2CppCodeRegistration` / `Il2CppMetadataRegistration`, resolves method addresses, field offsets and `Il2CppType`s |
| `dump.py` | writes C#-like declarations with field offsets, constant values and method addresses |
| `disasm.py` | capstone disassembly annotated with method names (including generic instances), string literals and metadata references |
| `pid.py` | dumps `<PrivateImplementationDetails>` array-initialiser data (the piece-square tables) |
| `extract.sh` | runs all of the above on `baseapk.apk` into `build/` |

The decompilation in `src/` was read from that disassembly by hand, method by method. Every
method carries its address (`// @ 0x…`), and quirks of the original are kept and marked
`// NOTE:`. Places where the port differs for practical reasons are marked `// PORT:`.

## The engine (`src/`)

| file | original |
|---|---|
| `cgEngine.cs` | `cgEngine`: root move loop, depth selection, `_alfaBeta`, move sorting |
| `cgBoardEval.cs` | `cgBoard.Evaluate`, `cgBoard.getBoardKey` |
| `cgBoard.cs` | board state, `move` / `revert`, castling rights, `isChecked` |
| `cgBoardLegal.cs` | `findLegalMoves`, `findStrictLegalMoves`, `removeIllegalMoves`, `verifyLegality`, ray helpers |
| `cgMoveGen.cs` | `cgGlobal`, `cgValueModifiers` (piece values and tables), `cgMoveGenerator`, `cgMoveSet`, move classes |
| `cgChessBoardScript.cs` | random army generation (`getRandomBoard`), rank maths, seeds, Unity's RNG; the rest is documentation |
| `UnityShim.cs` | stand-ins so the generator runs outside Unity with the same random sequence |

### How it plays

* **Search.** Plain fail-hard minimax with alpha-beta. White always maximises. There is no
  quiescence search, no iterative deepening and no time control.
* **Depth per root move.** If there are 9 or fewer legal root moves, every move gets
  `SearchDepthEndGame`. Otherwise a move whose generator score is above 20 (captures, castling,
  some positional jumps) gets `SearchDepthStrong`, and the rest get `SearchDepthWeak`. The code
  defaults are 4/4/5, but the shipped scene overrides them to **3/5/5**. Nothing in the game
  changes them, so difficulty only affects the armies.
* **Memoisation.** `prevStateValues` maps a hash of the 64 squares to a score, and lives for one
  root search. The key ignores side to move, castling state and remaining depth, and fail-hard
  cut-off bounds are stored as if exact. So a position first seen deep inside one root move's
  subtree, at shallow depth with a narrow window, returns that bogus score whenever it is reached
  again. This is the engine's main blind spot. It is why it sometimes leaves a queen to be
  captured in self-play.
* **Evaluation.** Material (P 1000, N 3000, B 3050, R 5000, Q 9000, K 50000) plus piece-square
  tables for P/N/B/R. Black uses the same table entry as White, with no vertical flip. There are
  also castling-rights bonuses (+15 long, +22 short) and +85 for having castled. Queens and kings
  have no positional term; `Positions_Queen` exists but is never read.
* **Move generation.** Precomputed rays per piece and square (`allHypotheticalMoves`, keyed by
  strings like `"512"`). `findLegalMoves` is pseudo-legal with partial pin and check handling.
  `findStrictLegalMoves` additionally verifies king moves by playing them. Inside the search,
  only the maximising side switches to strict generation, and only once 16 or fewer pieces
  remain. Black always searches with pseudo-legal moves, so it can walk into checks it thinks
  are safe, and the reverse.
* **Move ordering.** Moves are sorted by generator score descending: captures 127, king
  captures 50, castling 30, otherwise the piece-square delta. Root moves are searched from the
  end of the list and finally sorted by score with `List.Sort`, which is unstable.

Other observations (bugs in the original, kept as they are) are listed in the `NOTE:` comments.

### Random armies (`getRandomBoard`)

The game seeds `UnityEngine.Random` (xorshift128, ported from `libunity.so`) and builds armies
from a template with kings fixed on e1/e8. Each back-rank square gets a random P/R/N/B/Q, and
pawn-rank squares get mostly pawns. Downgrade and upgrade rolls then depend on the target rank.
The board is regenerated until the material difference, converted with
`INT_TO_RANK(x) = round((x+30)*1.66)`, lands 0–3 above the target. Rank 50 means equal
material; higher ranks give Black more. The player is always White; the AI always plays Black.
See `notes-game-layer.md` for details: modes, seeds, rank progression.

## Beating it

A minimax engine assumes the opponent finds the best reply. `cgEngine` is deterministic: given
a position it always plays the same move. So both engines here **run `cgEngine` itself** at
every opponent turn in their search, instead of guessing what it might do (opponent-model
search).

### Fast model (`exploit/FastModel.cs`)

`FastBoard` re-implements `cgBoard` and `cgEngine` line by line without the allocations. Moves
are structs in pooled arrays, the mutable per-move state is kept in an undo stack, move sets are
flat arrays, the evaluation is incremental, and the AI's position cache is an open-addressing
hash map. It keeps every quirk, including the exact move-generation order and the same unstable
sorts, so it must pick exactly the same move. `harness check N` compares the two on N random
reachable positions, including their strictly legal move lists. It is about 3× faster (≈160 ms
per AI move at depths 3/5/5).

### Planner (`exploit/Planner.cs`), the current engine

Because the opponent's reply is fixed, a line "our move, its reply, our move, …" is determined
entirely by our moves. The search tree only branches on our side, and a win is simply a line
after which the opponent has no legal move. In Really Bad Chess both mate and stalemate lose.

* **Best-first search.** Each queue entry is a position and one of our candidate moves. Its
  priority is a cheap estimate: quiescence search, the opponent's legal-move count, minus a
  penalty per move of depth. Calling the model, the expensive step, happens only when an entry
  reaches the front.
* **Shortest win.** Once a win in N is known, only shorter lines are explored, and the depth
  penalty rises (`DepthPenaltyAfterWin`) to make the search broader.
* **Plans carry over.** The best win is kept as a plan across turns. Since the model is exact,
  the plan stays valid while the opponent replies as predicted, and later turns only look for
  something shorter.
* **Fallback.** Without a known win, it plays towards the best position in the explored tree.
  Every such position is genuinely reachable, because the replies along the way are the real
  ones.
* **Parameters.** `SecondsPerMove` (default 300) is the time budget for each move. It also
  takes the number of threads, the opponent's depths and the penalty weights. A search stops
  early when no shorter win can exist.

The leaf evaluation is material plus a mating term when ahead: squares around the enemy king
that are attacked or blocked, the enemy king pushed to the edge, our king brought closer.

### First version (`exploit/Exploiter.cs`)

Two of our moves deep against the faithful model, scored by a conventional pessimistic
alpha-beta search. It is kept for comparison; see the results below.

## Running

```sh
# planner: ranks, games per rank, first seed, seconds per move [, threads]
dotnet run -c Release --project harness -- plan 0,30,60 2 1000 120
# check the fast model against the faithful port on 100 random positions
dotnet run -c Release --project harness -- check 100
# first version: ranks, games per rank, first seed
dotnet run -c Release --project harness -- 0,30,60 2 1000
```

The harness generates ranked boards with the game's own generator. Our engine plays White and
the faithful `cgEngine` (depths 3/5/5) plays Black. The game is played to a result, and every
AI reply is checked against the planner's prediction.

## Results

Run with `dotnet run -c Release --project harness -- 0,10,20,30,40,50,60,70,80,90,100 3 5000`: 33
ranked boards from the game's own generator, three seeds per rank. Rank 0 gives White the
stronger army; rank 100 gives Black about 30 points more material, for example several extra
queens. The exploiter played White and `cgEngine` played Black at the shipped depths 3/5/5.

| rank | games | won | moves to win |
|---:|---:|---:|---|
| 0 | 3 | 3 | 9, 10, 7 |
| 10 | 3 | 3 | 8, 19, 6 |
| 20 | 3 | 3 | 4, 5, 10 |
| 30 | 3 | 3 | 9, 15, 7 |
| 40 | 3 | 3 | 11, 11, 11 |
| 50 | 3 | 3 | 9, 8, 10 |
| 60 | 3 | 3 | 8, 14, 12 |
| 70 | 3 | 3 | 10, 20, 15 |
| 80 | 3 | 3 | 17, 10, 14 |
| 90 | 3 | 3 | 19, 13, 32 |
| 100 | 3 | 3 | 14, 30, 19 |
| **all** | **33** | **33** | **mean 12.1, max 32** |

Each game took 0.5–7 minutes on 4 cores, almost all of it spent running the model. The win
condition used is the game's: the side to move with no legal move loses, whether mated or
stalemated.

Caveat: the port was checked by reading the disassembly and by standard perft counts (20 / 400
/ 8902 from the initial position). It was not checked move-for-move against the running game,
so any decompilation error would make the model diverge from the real AI in some positions.
