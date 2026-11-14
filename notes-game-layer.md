# Really Bad Chess: board generation, difficulty, engine strength

C# source is in `rbc.cs`, in the same directory. Code addresses refer to `libil2cpp.so` (x86_64) unless they say `libunity.so`.

## 1. RNG
- The generator uses **UnityEngine.Random** only. It does not use System.Random. Seeding goes through the obsolete `Random.seed = x` setter, which is the same native code as `InitState`.
- Native code in libunity.so x86_64 (found through the icall name table):
  - **Seeding:** xorshift128. `s0=seed; s1=s0*1812433253+1; s2=s1*…+1; s3=s2*…+1`.
  - **Next():** `t=s0^(s0<<11); s0=s1; s1=s2; s2=s3; s3=s3^(s3>>19)^t^(t>>8)`.
  - **value:** `(Next() & 0x7FFFFF) * (1/8388607f)`. The range is [0,1] inclusive.
  - **Range(float a, float b):** `a*t + (1-t)*b` with `t` taken from `value`. Note that this is reversed compared with a usual lerp.
  - **Range(int a, int b):** `a + Next() % (b-a)` when a<b. When a>b it is `a - Next()%(a-b)`. Otherwise it returns a.
- **Seeds (MyRandom):**
  - `getRandomSeed` = `(Year+Millisecond)*1e6 + DayOfYear*1000 + Hour+Minute+Second - 2016000000`. Integer overflow wraps.
  - `getDailySeed(back)` = `int.Parse(Now.AddDays(-back).ToString("yyyyMMdd"))`.
  - `getWeeklySeed(back)`: go back `7*back` days, then go back to the Monday, then return `int.Parse(yyyy+"0000") + ISO-ish week + 10000000`. The week number comes from `GetWeekOfYear(FirstFourDayWeek, Monday)` in the current culture.

## 2. getRandomBoard(seed, targetRank, backSegments) @ 0x15a785d
**Target by mode.** The mode is the string in `modeName.text`.

| Mode | Seed | target |
|---|---|---|
| ranked | `seed==-1 ? getRandomSeed()` | `targetRank`, or the saved `playerRank` (default `INT_TO_RANK(-21)`=15, clamped ≥0) when `targetRank==-1`. Also re-saves `playerRank`. |
| freeplay, versus | `getRandomSeed()` | `freeplayDifficultySlider.curDifficulty` (the targetRank argument is ignored) |
| weekly | `getWeeklySeed(backSegments)` | `Random.Range(50,70)`, then the RNG is **re-seeded with the same seed** |
| daily | `getDailySeed(backSegments)` | `Random.Range(35,48)`, then re-seeded. Also sets `currentlyLoadedSeed`. |
| other | **no seeding** | 0 |

`fullStrength` is false only when the mode is ranked and target < `INT_TO_RANK(-5)` (=42).

**Rank helpers.**
- `INT_TO_RANK(x) = Math.Round((float)(x+30)*1.66f)`, using round-half-to-even. So rank 50 = "even material", and one material point ≈ 1.66 rank.
- `INT_TO_RANKADJUST(x) = Math.Round(x*1.66f)`.

**Template.** The template has 64 squares (a8..h1):
- Back ranks: `±9` everywhere except a fixed king `±6` on e8/e1.
- Pawn ranks: `±7` on the a, b, c, g and h files, and `±8` on the d, e and f files.
- Middle: zeros.

**Probabilities** (t = target as a float):
- target>0: `blackUpgrade = t/50`. `whiteDowngrade = t/100` only if !fullStrength.
- target<0: `whiteUpgrade = -t/50`. `blackDowngrade = -t/100` only if !fullStrength.
- With real ranks (≥0), only blackUpgrade and, in ranked play below 42, whiteDowngrade are ever non-zero. A target of 50 or more means blackUpgrade ≥ 1, so every eligible black piece gets upgraded.

**Per-square rolls.** Squares are processed in template order. The order of RNG calls is exactly as written in `rbc.cs`.
- **Back rank (`±9`):**
  1. `p = Range(1,6)`, so P/R/N/B/Q are uniform.
  2. If `down > value`, downgrade: Q→R, R→`Range(3,5)` (N/B), anything else→P.
  3. If `up > value`, upgrade: N/B→R, R→Q, P→`Range(3,5)`.
  4. The upgrade check draws `value` even when nothing is changed.
- **Pawn rank (`±7` outer files), target>39:**
  1. `p = Range(1,4)`.
  2. If p==2 and value>0.1, p becomes knight or bishop: `min(floor(Range(3f,5.5f)),4)`, which gives N 40% / B 60%. Otherwise p==2 stays a rook (only 10% of the time).
- **Pawn rank (`±7` outer files), target≤39:** `Range(1,4)` is drawn and then **discarded**. After that it is the `±8` rule.
- **Pawn rank (`±8` centre files):** `p = Range(1,3)`. If p==2, p becomes knight or bishop as above. So pawn ranks have no rooks at target≤39.
- **Then, for both pawn-rank kinds:**
  1. If `down > value`, p becomes a pawn.
  2. `r = value` is always drawn.
  3. If p is a pawn and `up > r`, p becomes `floor(Range(3,5.5))`, with ≥5 mapped to 4 (N/B).

**Values and counts.**
- Material uses the table at 0x7d56a4: P1 R5 N3 B3 Q9 K0.
- `pawnCount` counts black pawns when target<0, or white pawns when target>0. It is only active when fullStrength.

**Accept/reject loop.** The generator repeats:
- `spread = INT_TO_RANK(blackValue - whiteValue)` is computed after each pass.
- The loop accepts the board only when **0 ≤ spread − target ≤ 3** **and** NOT(pawnCount>4 && attempts<10000) **and** blackValue≠0.
- The spread window has **no attempt cap**. Only the pawn rule gives up after 10000 tries.

**Result.**
- `Debug.Log(attempts)`.
- `new cgBoard(list)`. In the cgBoard ctor, `_whiteTurnToMove=true` and `remainingPieces=32`.
- Then it sets `seed`, `targetForGeneration=target`, `currentGameSpread=spread`, and `currentGameProxToSpread=spread-target` (0..3).
- `cgGlobal.pieceSet=0`.

**Python check.** I ported the generator to Python (`tools/sim.py` in the scratchpad). It terminates and gives sensible material spreads; for example, seed 12345 with target 50 needs 62 attempts.

## 3. Difficulty → engine strength
- **No code in Assembly-CSharp writes `SearchDepthWeak`, `SearchDepthStrong` or `SearchDepthEndGame`.** I disassembled all 13k methods and checked every `+0x210` (`_engine`) access. Outside cgEngine, the only engine writes are to `paused` (+0x40) and `paused2` (+0x41) from GameMenuObject/AdHandler/ButtonSlider, `updateLoadBar` (SuggestMove), and `manager` (Awake).
- **Difficulty and rank only change the armies** (the material spread). The AI search is fixed.
- **Depth per root move** (`_processThreadded`):
  - `Moves.Count <= 9` → `SearchDepthEndGame`.
  - Otherwise, `positionalVal > 20` → `SearchDepthStrong`.
  - Otherwise → `SearchDepthWeak`.
- **Serialized values in the scene (see §4):** Weak=3, Strong=5, EndGame=5. The code defaults (4/4/5) are overridden.

**Who the engine plays.**
- `startMode` sets `Mode = PlayerVsEngine`, so the human is white and the engine is black. "versus" sets `PlayerVsPlayer`, so there is no engine (slider 0..100, 50 = even).
- After a human move (`_squareDown` / `_pieceUp`):
  - PvE → `_engine.MakeMove(_abstractBoard, false, _engineCallback)`.
  - EvP → `MakeMove(…, true, …)`.
- `_start()`:
  - PvE and it is black to move → `MakeMove(false)`.
  - EvP and it is white to move → `MakeMove(true)`.
- `loadBoard` (restoring a game, not versus, movesBack==0) and `_pasteGameFromClipboard` call `MakeMove(false)` if it is black to move.
- `_engineCallback` → `_makeMove(move)`.
- `SuggestMove` (hint) uses the same engine and the same depths: `MakeMove(board, board._whiteTurnToMove, _engineSuggestion)` with `updateLoadBar=false`.

**Rank progression** (ranked mode, `handleGameOver`). `prox` = `currentGameProxToSpread`.
- **Win:** prox≤-3 → +1. prox<0 → +3. prox 0..1 → +5. prox≥2 → +8.
  - If the new rank is higher than `highscoreSpread` (default -999), it is saved as `highscoreSpread`. That value is the freeplay slider maximum.
- **Loss or surrender** (and also any other result code, probably a draw): prox≤-3 → −5. prox<0 → −3. Otherwise → −2. The rank floor is 0.
- In practice prox is always 0..3, so a win is +5 or +8 and a loss is −2.

**DifficultySlider.**
- `Awake`: min = INT_TO_RANK(-30) = 0, max = `highscoreSpread` (default 17), cur = min.
- `setDifficulty(f)`: `cur = round(min + (max-min)*f)`.
- Freeplay: `setMaxDifficulty(-1)`.
- Versus: `setMaxDifficulty(100)`, cur = saved `versusSpread` (default 50).

## 4. Serialized cgEngine values (UnityPy 1.25.4 on assets/bin/Data/data.unity3d)
- **Where it is:** there is one cgEngine MonoBehaviour. It is in `level1`, pathID 2921, on GameObject "Chessboard" (the same object as cgChessBoardScript, which finds it with `GetComponent<cgEngine>()`). Its script is MonoScript globalgamemanagers.assets #1026.
- **Layout:** the raw data has no type tree. It is 92 bytes. Each bool/byte is padded to 4 bytes, and `List<cgSimpleMove> Moves` is not serialized, which accounts for exactly 92 bytes.

| field | value |
|---|---|
| updateLoadBar | 1 (true) |
| MoveAsWhite | 1 (true; overwritten by every MakeMove) |
| Finished | 0 |
| **SearchDepthWeak** | **3** |
| **SearchDepthStrong** | **5** |
| **SearchDepthEndGame** | **5** |
| BoardStatesExamined, CurrentBoardValue, MovesLeftToAnalyze, TotalMovesToAnalyze | 0 |
| paused, paused2, processingComplete | 0 |
| LoadBar | PPtr{0, 510} |
| loadBarCompleteWidth | 1.2 |
| moveCompleteCircle | PPtr{0, 169} |
| manager | null (set in cgChessBoardScript.Awake) |

The DifficultySlider (GO "NewBoardFreeplay") has `curDifficulty=0` serialized, and Awake overwrites it.

## 5. Uncertainties / quirks
- **Floor and clamp idiom.** `floor(Range(3f,5.5f))` is compiled as `roundss …, 9` (floor). It is probably `Mathf.FloorToInt` or `(int)Math.Floor`; both behave the same. The two forms of the clamp (`>=4→4` and `>=5→4`) give the same result.
- **Float evaluation.** The float probabilities are computed with float32 division (`t/50f`, `t/-100f`, …).
- **The 0.1 comparison.** It is done in double: `(double)Random.value > 0.1`.
- **Extra RNG draw.** For `±7` squares at target≤39, an extra `Range(1,4)` is drawn and discarded. This is a quirk to keep if you want to reproduce boards exactly.
- **Odd sign choice.** The pawn-count rule counts the pawns of the side that is *not* upgraded (white when target>0). It rejects a board when that side has ≥5 pawns.
- **Draws.** A draw or unknown result code in ranked mode reduces the rank, the same as a loss.
- **Spread is in rank units.** `currentGameSpread` is measured in rank units, not material. A ranked target of 15, for example, means black has about 21 fewer material points than white.
- **Different builds.** The arm64/armv7 libunity builds were not checked, but Unity's Random is the same on all platforms.
