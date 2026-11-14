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

## The exploit engine (`exploit/Exploiter.cs`)

A minimax engine assumes the opponent finds the best reply. `cgEngine` is deterministic: given
a position it always plays the same move. So the exploiter **runs the decompiled `cgEngine`
itself** at every opponent turn in its search (opponent-model search). It only branches over
its own moves:

1. Each legal root move is played, the model's actual reply is computed, and the result is
   scored by a conventional alpha-beta search with quiescence. That search is pessimistic,
   because it assumes best defence beyond the modelled ply.
2. The best few candidates are searched one more of our moves deep against the model.
   Candidates at that level are pre-ordered by the same conventional search.
3. Positions where the opponent has no legal move score as wins: in Really Bad Chess both mate
   and stalemate lose. Faster wins score higher.

Model replies are cached per position and root moves are searched in parallel. The leaf
evaluation is ordinary material plus a mating-technique term (drive the enemy king to the
edge, bring ours closer), so once the model's mistakes have won material the engine converts
quickly.

## Running

```sh
dotnet run -c Release --project harness -- 0,30,60 2 1000
#   ranks to test, games per rank, first seed (append any 4th argument to print every game)
```

`harness/Program.cs` generates ranked boards with the game's own generator, gives the
exploiter White and `cgEngine` (depths 3/5/5) Black, and plays to a result.
