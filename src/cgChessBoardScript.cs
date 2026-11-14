// Really Bad Chess - decompiled from libil2cpp.so (x86_64) + libunity.so (x86_64)
// Scope: cgChessBoardScript.getRandomBoard and every helper it calls, rank helpers,
//        DifficultySlider, MyRandom, UnityEngine.Random (native), engine invocation sites.
// Faithful decompilation: control flow, call order (=> RNG draw order) and quirks preserved.
// Board index 0 = a8 ... 63 = h1. Piece codes 1 P,2 R,3 N,4 B,5 Q,6 K; negative = black.

using System;
using System.Collections.Generic;
using System.Globalization;
using Mathf = UnityEngine.Mathf;

// =====================================================================================
// cgChessBoardScript (partial)
// =====================================================================================
public partial class cgChessBoardScript /* : MonoBehaviour */
{
    public static cgChessBoardScript Instance;
    public static int   RANK_ADJUST;      // static +0x8   = 30   (set in .cctor)
    public static float RANK_MULT;        // static +0xc   = 1.66f (0x3FD47AE1)

    // @ 0x15b97ee
    static cgChessBoardScript()
    {
        RANK_ADJUST = 30;
        RANK_MULT = 1.66f;   // never written anywhere else in Assembly-CSharp
    }

    // @ 0x15af9ec
    public static int INT_TO_RANK(int input)
    {
        // float multiply, then System.Math.Round(double) = round-half-to-even
        float f = (float)(input + RANK_ADJUST) * RANK_MULT;
        return (int)Math.Round((double)f);
        // values: -30->0, -21->15, -20->17, -9->35, -5->42 (41.5 rounds to even), -1->48, 0->50, 12->70
    }

    // @ 0x15b1cbf
    public static int INT_TO_RANKADJUST(int input)
    {
        float f = (float)input * RANK_MULT;
        return (int)Math.Round((double)f);      // 1->2, 2->3, 3->5, 5->8
    }

    // static readonly table at 0x7d56a4: { 1, 5, 3, 3, 9 }  (P, R, N, B, Q)
    static readonly int[] s_pointValues = { 1, 5, 3, 3, 9 };

    // @ 0x15afb20   (compiler-generated switch; inlined verbatim into getRandomBoard)
    public int getPointValueForPiece(int piece)
    {
        switch (piece)
        {
            case 1: return 1;
            case 2: return 5;
            case 3: return 3;
            case 4: return 3;
            case 5: return 9;
            default: return 0;   // king (6) and anything else = 0
        }
    }

    // @ 0x15a785d
    // Callers:
    //   _start()                       -> getRandomBoard(-1, -1, 0)
    //   loadBoard / _pasteGameFromClipboard (restoring a save "...|seed|target|...")
    //                                  -> getRandomBoard(int.Parse(p[1]), int.Parse(p[2]), 0)
    //   setBoardToDailyBack(back)      -> getRandomBoard(-1, -1, back)
    public cgBoard getRandomBoard(int seed, int targetRank, int backSegments)
    {
        tutorialManager.setupTutorialIndicator();

        int  target;            // [rsp+0x28]
        bool fullStrength;      // r15b: false only for "ranked" with target < INT_TO_RANK(-5) (=42)

        if (modeName.text == "ranked")
        {
            DateTime unused = DateTime.Now;
            if (seed == -1) seed = MyRandom.getRandomSeed();
            UnityEngine.Random.seed = seed;                       // == InitState(seed)
            int playerRank = SaveData.GetInt("playerRank", INT_TO_RANK(-21));   // default 15
            if (playerRank < INT_TO_RANK(-30)) playerRank = INT_TO_RANK(-30);    // clamp >= 0
            SaveData.SetInt("playerRank", playerRank);
            SaveData.SaveAsync();
            if (targetRank == -1) targetRank = playerRank;
            target = targetRank;
            fullStrength = target >= INT_TO_RANK(-5);           // >= 42
        }
        else if (modeName.text == "freeplay" || modeName.text == "versus")
        {
            DateTime unused = DateTime.Now;
            if (seed == -1) seed = MyRandom.getRandomSeed();
            UnityEngine.Random.seed = seed;
            target = freeplayDifficultySlider.curDifficulty;    // targetRank argument ignored
            fullStrength = true;
        }
        else if (modeName.text == "weekly")
        {
            if (seed == -1) seed = MyRandom.getWeeklySeed(backSegments);
            UnityEngine.Random.seed = seed;
            target = UnityEngine.Random.Range(INT_TO_RANK(0), INT_TO_RANK(12));  // [50,70)
            UnityEngine.Random.seed = seed;                     // re-seed: board RNG starts fresh
            fullStrength = true;
        }
        else if (modeName.text == "daily")
        {
            fullStrength = true;
            if (seed == -1) seed = MyRandom.getDailySeed(backSegments);
            UnityEngine.Random.seed = seed;
            target = UnityEngine.Random.Range(INT_TO_RANK(-9), INT_TO_RANK(-1)); // [35,48)
            UnityEngine.Random.seed = seed;
            currentlyLoadedSeed = MyRandom.getDailySeed(backSegments);
        }
        else
        {
            // NOTE: no Random.seed call on this path - RNG continues from whatever state it had.
            fullStrength = true;
            target = 0;
        }

        // Template: -9 / 9 = random back-rank piece, -7 / 7 = random pawn-rank piece (outer files
        // a,b,c,g,h), -8 / 8 = random pawn-rank piece (centre files d,e,f), +-6 = king (fixed on e-file).
        List<sbyte> template = new List<sbyte>();
        template.Add(-9); template.Add(-9); template.Add(-9); template.Add(-9);
        template.Add(-6); template.Add(-9); template.Add(-9); template.Add(-9);
        template.Add(-7); template.Add(-7); template.Add(-7); template.Add(-8);
        template.Add(-8); template.Add(-8); template.Add(-7); template.Add(-7);
        for (int i = 0; i < 32; i++) template.Add(0);
        template.Add(7); template.Add(7); template.Add(7); template.Add(8);
        template.Add(8); template.Add(8); template.Add(7); template.Add(7);
        template.Add(9); template.Add(9); template.Add(9); template.Add(9);
        template.Add(6); template.Add(9); template.Add(9); template.Add(9);

        List<sbyte> board = new List<sbyte>();

        float t = (float)target;                 // [rsp+0x18] (later reused, see below)
        float whiteUpgrade   = 0f;               // [rsp+0x18] after setup
        float whiteDowngrade = 0f;               // [rsp+0x30]
        float blackUpgrade   = 0f;               // [rsp+0x20]
        float blackDowngrade = 0f;               // [rsp+0x24]
        float tmpDown = 0f;
        if (!fullStrength)
        {
            blackDowngrade = t / -100f;
            tmpDown = t / 100f;
        }
        float tmp2 = 0f;
        bool targetNeg = target < 0;
        bool targetPos = target > 0;
        if (target != 0)
        {
            blackUpgrade = t / 50f;
            tmp2 = tmpDown;
        }
        if (target < 0)
        {
            whiteUpgrade = t / -50f;
            blackUpgrade = 0f;
            // blackDowngrade keeps t/-100 (if !fullStrength), whiteDowngrade stays 0
        }
        else
        {
            whiteUpgrade = 0f;
            blackDowngrade = 0f;
            whiteDowngrade = tmp2;               // t/100 only if target>0 && !fullStrength
        }
        // Net effect:
        //   target > 0 : blackUpgrade = t/50, whiteDowngrade = (!fullStrength ? t/100 : 0)
        //   target < 0 : whiteUpgrade = -t/50, blackDowngrade = (!fullStrength ? -t/100 : 0)
        //   target == 0: all zero
        bool countBlackPawns = targetNeg & fullStrength;   // [rsp+0x17]
        bool countWhitePawns = targetPos & fullStrength;   // [rsp+0x16]

        int attempts = 0;      // edx / [rsp+0x3c]
        int pawnCount = 0;     // r13d
        int spread = 0;        // r12d
        int blackValue = 0;    // [rsp+0x1c]
        int whiteValue;        // [rsp+0x34]
        int prox;              // ebx
        for (;;)
        {
            prox = spread - target;
            // loop exit: 0 <= prox <= 3 (unsigned compare), pawn rule satisfied (or >= 10000 tries),
            // and at least one generation pass done (blackValue != 0)
            if ((uint)prox <= 3u && !(pawnCount > 4 && attempts < 10000) && blackValue != 0)
                break;

            attempts++;
            board.Clear();
            whiteValue = 0;
            blackValue = 0;
            pawnCount = 0;

            foreach (sbyte sq in template)
            {
                int p;
                switch (sq)
                {
                    // ---------------- black pawn rank, outer files ----------------
                    case -7:
                    {
                        p = UnityEngine.Random.Range(1, 4);                      // 1..3
                        if (target > 39)
                        {
                            if (p == 2 && (double)UnityEngine.Random.value > 0.1)
                            {
                                p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                                if (p >= 4) p = 4;
                            }
                            // else p==2 stays a ROOK (10%) - rooks on the pawn rank only when target > 39
                        }
                        else
                        {
                            // NOTE: the Range(1,4) result above is discarded (an extra RNG draw)
                            p = UnityEngine.Random.Range(1, 3);                  // 1..2
                            if (p == 2)
                            {
                                p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                                if (p >= 4) p = 4;
                            }
                        }
                        if (blackDowngrade > UnityEngine.Random.value) p = 1;
                        float r = UnityEngine.Random.value;
                        if (p == 1 && blackUpgrade > r)
                        {
                            p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                            if (p >= 5) p = 4;
                        }
                        blackValue += getPointValueForPiece(p);
                        board.Add(Convert.ToSByte(-p));
                        pawnCount += (p == 1 && countBlackPawns) ? 1 : 0;
                        break;
                    }
                    // ---------------- black pawn rank, centre files ----------------
                    case -8:
                    {
                        p = UnityEngine.Random.Range(1, 3);                      // 1..2
                        if (p == 2)
                        {
                            p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                            if (p >= 4) p = 4;
                        }
                        if (blackDowngrade > UnityEngine.Random.value) p = 1;
                        float r = UnityEngine.Random.value;
                        if (p == 1 && blackUpgrade > r)
                        {
                            p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                            if (p >= 5) p = 4;
                        }
                        blackValue += getPointValueForPiece(p);
                        board.Add(Convert.ToSByte(-p));
                        pawnCount += (p == 1 && countBlackPawns) ? 1 : 0;
                        break;
                    }
                    // ---------------- black back rank ----------------
                    case -9:
                    {
                        p = UnityEngine.Random.Range(1, 6);                      // 1..5 uniform
                        if (blackDowngrade > UnityEngine.Random.value)
                        {
                            if (p == 5) p = 2;                                   // Q -> R
                            else if (p == 2) p = UnityEngine.Random.Range(3, 5); // R -> N/B
                            else p = 1;                                          // P/N/B -> P
                        }
                        if (blackUpgrade > UnityEngine.Random.value)
                        {
                            if (p == 3 || p == 4) p = 2;                         // N/B -> R
                            else if (p == 2) p = 5;                              // R -> Q
                            else if (p == 1) p = UnityEngine.Random.Range(3, 5); // P -> N/B
                        }
                        blackValue += getPointValueForPiece(p);
                        board.Add(Convert.ToSByte(-p));
                        pawnCount += (p == 1 && countBlackPawns) ? 1 : 0;
                        break;
                    }
                    // ---------------- white pawn rank, outer files ----------------
                    case 7:
                    {
                        p = UnityEngine.Random.Range(1, 4);
                        if (target > 39)
                        {
                            if (p == 2 && (double)UnityEngine.Random.value > 0.1)
                            {
                                p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                                if (p >= 4) p = 4;
                            }
                        }
                        else
                        {
                            // NOTE: first Range(1,4) discarded, as for black
                            p = UnityEngine.Random.Range(1, 3);
                            if (p == 2)
                            {
                                p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                                if (p >= 4) p = 4;
                            }
                        }
                        if (whiteDowngrade > UnityEngine.Random.value) p = 1;
                        float r = UnityEngine.Random.value;
                        if (p == 1 && whiteUpgrade > r)
                        {
                            p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                            if (p >= 5) p = 4;
                        }
                        whiteValue += getPointValueForPiece(p);
                        board.Add(Convert.ToSByte(p));
                        pawnCount += (p == 1 && countWhitePawns) ? 1 : 0;
                        break;
                    }
                    // ---------------- white pawn rank, centre files ----------------
                    case 8:
                    {
                        p = UnityEngine.Random.Range(1, 3);
                        if (p == 2)
                        {
                            p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                            if (p >= 4) p = 4;
                        }
                        if (whiteDowngrade > UnityEngine.Random.value) p = 1;
                        float r = UnityEngine.Random.value;
                        if (p == 1 && whiteUpgrade > r)
                        {
                            p = Mathf.FloorToInt(UnityEngine.Random.Range(3f, 5.5f));
                            if (p >= 5) p = 4;
                        }
                        whiteValue += getPointValueForPiece(p);
                        board.Add(Convert.ToSByte(p));
                        pawnCount += (p == 1 && countWhitePawns) ? 1 : 0;
                        break;
                    }
                    // ---------------- white back rank ----------------
                    case 9:
                    {
                        p = UnityEngine.Random.Range(1, 6);
                        if (whiteDowngrade > UnityEngine.Random.value)
                        {
                            if (p == 5) p = 2;
                            else if (p == 2) p = UnityEngine.Random.Range(3, 5);
                            else p = 1;
                        }
                        if (whiteUpgrade > UnityEngine.Random.value)
                        {
                            if (p == 3 || p == 4) p = 2;
                            else if (p == 2) p = 5;
                            else if (p == 1) p = UnityEngine.Random.Range(3, 5);
                        }
                        whiteValue += getPointValueForPiece(p);
                        board.Add(Convert.ToSByte(p));
                        pawnCount += (p == 1 && countWhitePawns) ? 1 : 0;
                        break;
                    }
                    default:
                        board.Add(sq);       // 0, -6, 6 copied verbatim (kings always on e8 / e1)
                        break;
                }
            }
            spread = INT_TO_RANK(blackValue - whiteValue);
        }

        UnityEngine.Debug.Log(attempts);                  // boxed int
        cgBoard result = new cgBoard(board);
        result.seed = seed;
        result.targetForGeneration = target;
        result.currentGameSpread = spread;
        result.currentGameProxToSpread = prox;            // = spread - target, normally 0..3
        cgGlobal.pieceSet = 0;
        return result;
    }

#if UNITY_ONLY   // the rest of cgChessBoardScript, cgEngine/cgBoard/DifficultySlider excerpts: documentation only
    // @ 0x15a76ef
    void _start()
    {
        UnityEngine.Debug.Log("start!");
        _setBoardTo(getRandomBoard(-1, -1, 0));
        if (Mode == BoardMode.EngineVsPlayer)            // 2
        {
            if (whiteTurnToMove)
                _engine.MakeMove(_abstractBoard, true, _engineCallback);
        }
        else if (Mode == BoardMode.PlayerVsEngine)       // 1
        {
            if (!whiteTurnToMove)
                _engine.MakeMove(_abstractBoard, false, _engineCallback);
        }
    }

    // @ 0x15aa2df
    public void StartGame(BoardMode mode)
    {
        switch (mode)
        {
            case BoardMode.PlayerVsPlayer: StartPlayerVsPlayer(); break;   // Mode=0; _start()
            case BoardMode.EngineVsPlayer: StartEngineVsPlayer(); break;   // Mode=2; _start()
            case BoardMode.PlayerVsEngine: StartPlayerVsEngine(); break;   // Mode=1; _start()
        }
    }

    // @ 0x15a76d6 / 0x15a76e4 / 0x15a76ac
    bool _humanPlayerIsBlack { get { return ((int)Mode & ~2) == 0; } }   // PvP or EvP
    bool _humanPlayerIsWhite { get { return (uint)Mode < 2u; } }         // PvP or PvE
    public bool playerCanMove
    {
        get { return (_humanPlayerIsBlack && !whiteTurnToMove) || (_humanPlayerIsWhite && whiteTurnToMove); }
    }

    // _squareDown @ 0x15b5072 and _pieceUp @ 0x15b6917 (tail after a legal human move):
    //     undoButton.enable();
    //     _makeMove(move);
    //     if (Mode == BoardMode.PlayerVsEngine)      _engine.MakeMove(_abstractBoard, false, _engineCallback);
    //     else if (Mode == BoardMode.EngineVsPlayer) _engine.MakeMove(_abstractBoard, true,  _engineCallback);
    // _engineCallback @ 0x15b7291 -> _makeMove(move) (engine's move applied to the real board).

    // @ 0x15b40d9  (hint button: same engine, same depths)
    public void SuggestMove()
    {
        _engine.updateLoadBar = false;
        if (playerCanMove)
            _engine.MakeMove(_abstractBoard, _abstractBoard._whiteTurnToMove, _engineSuggestion);
    }

    // loadBoard @ 0x15acb38 (after restoring a saved game): if the mode is not "versus" (and
    // the daily-over check fails), movesBack == 0 and !_abstractBoard._whiteTurnToMove:
    //     _engine.MakeMove(_abstractBoard, false, _engineCallback);
    // _pasteGameFromClipboard @ 0x15af7c8: after LoadGame, if !_abstractBoard._whiteTurnToMove:
    //     _engine.MakeMove(_abstractBoard, false, _engineCallback);

    // startMode(string mode) @ 0x15abe63 (relevant parts):
    //     Mode = BoardMode.PlayerVsEngine;                       // human = white, engine = black
    //     ... if (mode == "freeplay") freeplayDifficultySlider.setMaxDifficulty(-1);
    //     ... if (mode == "versus") { Mode = BoardMode.PlayerVsPlayer; freeplayDifficultySlider.setMaxDifficulty(100); }
    //     ... loadBoard(mode, 0);
    // No code anywhere writes cgEngine.SearchDepthWeak/Strong/EndGame.

    // handleGameOver(int result) @ 0x15afb67, "ranked" branch (rank progression):
    //     int rank = SaveData.GetInt("playerRank", INT_TO_RANK(-21));
    //     int prox = _abstractBoard.currentGameProxToSpread;
    //     if (result == 2) {                                     // win
    //         int newRank;
    //         if (prox <= -3)      newRank = rank + 1;
    //         else if (prox < 0)   newRank = rank + INT_TO_RANKADJUST(2);   // +3
    //         else if (prox <= 1)  newRank = rank + INT_TO_RANKADJUST(3);   // +5
    //         else                 newRank = rank + INT_TO_RANKADJUST(5);   // +8
    //         if (newRank > SaveData.GetInt("highscoreSpread", -999))
    //             SaveData.SetInt("highscoreSpread", newRank);              // = freeplay slider max
    //         SaveData.SetInt("playerRank", newRank); SaveData.SaveAsync();
    //     } else {                                               // 0 surrender, 1 defeat, other (draw?) too
    //         int adj = prox <= -3 ? 3 : (prox < 0 ? 2 : 1);
    //         int newRank = rank - INT_TO_RANKADJUST(adj);                  // -5 / -3 / -2
    //         if (newRank < INT_TO_RANK(-30)) newRank = INT_TO_RANK(-30);   // floor 0
    //         SaveData.SetInt("playerRank", newRank); SaveData.SaveAsync();
    //     }
}

// =====================================================================================
// cgBoard(List<sbyte>) ctor @ 0x15996a9  (field initialisers included)
// =====================================================================================
public partial class cgBoard
{
    public cgBoard(List<sbyte> positions)
    {
        startingBoardState = new sbyte[64];
        squares = new sbyte[64];
        moves = new List<cgSimpleMove>();
        _whiteTurnToMove = true;
        remainingPieces = 32;
        seed = -1;                       // 16-byte init from 0x7d4840: { -1, -99, -99, -99 }
        targetForGeneration = -99;
        currentGameSpread = -99;
        currentGameProxToSpread = -99;
        if (positions != null)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                squares[i] = positions[i];
                startingBoardState[i] = positions[i];
            }
        }
        else
        {
            for (int i = 0; i < defaultStartPosition.Length; i++)
            {
                squares[i] = defaultStartPosition[i];
                startingBoardState[i] = defaultStartPosition[i];
            }
        }
        if (allHypotheticalMoves == null) _generateAllPossibleMoves();
    }
}

// =====================================================================================
// cgEngine - only the strength-related parts
// =====================================================================================
public partial class cgEngine /* : MonoBehaviour */
{
    // @ 0x159db6f   code defaults (overridden by scene data, see rbc_notes.md):
    //   SearchDepthWeak = 4; SearchDepthStrong = 4; SearchDepthEndGame = 5;
    // Serialized on GameObject "Chessboard" (level1, pathID 2921):
    //   updateLoadBar=1 MoveAsWhite=1 Finished=0 SearchDepthWeak=3 SearchDepthStrong=5 SearchDepthEndGame=5

    // @ 0x159961b
    public void MakeMove(cgBoard board, bool MoveAsWhiteP, Action<cgSimpleMove> callback)
    {
        paused = false;          // volatile
        Finished = false;
        _board = new cgBoard(null);
        board.copyTo(_board);
        _board_ref = board;
        MoveAsWhite = MoveAsWhiteP;
        _callback = callback;
        _startAnalysisThreadded();
    }

    // depth choice in _processThreadded @ 0x1599f0c (root moves iterated from last to first):
    //   byte depth = (Moves.Count <= 9) ? SearchDepthEndGame
    //              : (move.positionalVal > cgValueModifiers.AlphaBeta_Strong_Delineation /*20*/
    //                    ? SearchDepthStrong : SearchDepthWeak);
    //   move.val = _alfaBeta(move, depth, int.MinValue, int.MaxValue, !MoveAsWhite);
}

// =====================================================================================
// DifficultySlider (freeplay / versus target selection)
// =====================================================================================
public class DifficultySlider /* : MonoBehaviour */
{
    public cgChessBoardScript manager;   // 0x20
    public TMPro.TextMeshPro buttonText; // 0x28
    public ZUISlider slider;             // 0x30
    private int minDifficulty = -30;     // 0x38  (ctor @ 0x16181c1)
    private int maxDifficulty;           // 0x3c
    public int curDifficulty;            // 0x40  (serialized value 0)

    // @ 0x1617c1a
    void Awake()
    {
        minDifficulty = cgChessBoardScript.INT_TO_RANK(minDifficulty);                   // 0
        maxDifficulty = SaveData.GetInt("highscoreSpread", cgChessBoardScript.INT_TO_RANK(-20)); // default 17
        curDifficulty = minDifficulty;
    }

    // @ 0x1617cc4
    float getCurrentDifficulty()
    {
        return 0f + ((float)curDifficulty - (float)minDifficulty) / ((float)maxDifficulty - (float)minDifficulty);
    }

    // @ 0x1617d00
    public void setMaxDifficulty(int mD)
    {
        if (mD != -1)
        {   // versus: 0..100, "even" at 50
            maxDifficulty = mD;
            curDifficulty = SaveData.GetInt("versusSpread", 50);
            setDifficultyInt(curDifficulty);
        }
        else
        {   // freeplay: 0..best ranked rank ever reached
            maxDifficulty = SaveData.GetInt("highscoreSpread", cgChessBoardScript.INT_TO_RANK(-20));
        }
    }

    // @ 0x1617dec
    public void setDifficultyInt(int d)
    {
        curDifficulty = d;
        setText();
        slider.setDifficulty(0f + ((float)curDifficulty - (float)minDifficulty) / ((float)maxDifficulty - (float)minDifficulty));
    }

    // @ 0x161809e
    public void setDifficulty(float d)
    {
        float v = (float)(maxDifficulty - minDifficulty) * d + (float)minDifficulty;
        curDifficulty = (int)Math.Round((double)v);
        setText();
    }

    // @ 0x1617e30 (text only): versus -> "even" at 50, "white +(50-x)" below, "black +(x-50)" above,
    // then SaveData.SetInt("versusSpread", curDifficulty); freeplay -> "• reset board (rank <colored x>)".
    void setText() { /* UI only */ }
}

#else
}
#endif

// =====================================================================================
// MyRandom (seeds)
// =====================================================================================
public static class MyRandom
{
    // @ 0x161a02a
    public static int getRandomSeed()
    {
        DateTime now = DateTime.Now;
        unchecked
        {
            // NOTE: Millisecond is added to Year before the *1e6, and Hour/Minute/Second are simply
            // summed; int overflow wraps. Constant 0x87D64800 = -2016000000.
            return (now.Year + now.Millisecond) * 1000000 + now.DayOfYear * 1000
                   + now.Hour + now.Minute + now.Second - 2016000000;
        }
    }

    // @ 0x161a0ee
    public static int getDailySeed(int backDay)
    {
        return int.Parse(DateTime.Now.AddDays((double)(-backDay)).ToString("yyyyMMdd"));   // e.g. 20261114
    }

    // @ 0x161a17a
    public static int getWeeklySeed(int backWeek)
    {
        return getWeeklySeed(DateTime.Now.AddDays((double)(backWeek - backWeek * 8)));     // -7*backWeek days
    }

    // @ 0x161a1e6
    public static int getWeeklySeed(DateTime dateTime)
    {
        while (dateTime.DayOfWeek != DayOfWeek.Monday)
            dateTime = dateTime.AddDays(-1.0);
        int week = getWeekNumber(dateTime);
        return int.Parse(dateTime.ToString("yyyy") + "0000") + week + 10000000;            // e.g. 30260000+week
    }

    // @ 0x161a2d3
    public static int getWeekNumber(DateTime dateTime)
    {
        return System.Threading.Thread.CurrentThread.CurrentCulture.Calendar
               .GetWeekOfYear(dateTime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
    }
}

// =====================================================================================
// UnityEngine.Random - native implementation from libunity.so (x86_64), for reproduction
//   icall table: InitState/set_seed @0x6f87a1/0x6f8f71, Range(float) @0x6f87f0,
//   RandomRangeInt @0x6f885b -> 0x72b748, get_value @0x6f8879
// =====================================================================================
public static class UnityRandomReference
{
    static uint s0, s1, s2, s3;

    public static void InitState(int seed)        // also what the obsolete `Random.seed = x` setter does
    {
        unchecked
        {
            s0 = (uint)seed;
            s1 = s0 * 1812433253u + 1u;               // 0x6C078965
            s2 = s1 * 1812433253u + 1u;
            s3 = s2 * 1812433253u + 1u;
        }
    }

    static uint Next()                                 // xorshift128
    {
        uint t = s0 ^ (s0 << 11);
        s0 = s1; s1 = s2; s2 = s3;
        s3 = s3 ^ (s3 >> 19) ^ t ^ (t >> 8);
        return s3;
    }

    public static float value                          // [0,1] inclusive
    {
        get { return (float)(Next() & 0x7FFFFFu) * (1.0f / 8388607.0f); }   // mul by 1.1920930376e-07f
    }

    public static float Range(float min, float max)   // NOTE: min*t + (1-t)*max  (t=value)
    {
        float t = (float)(Next() & 0x7FFFFFu) * (1.0f / 8388607.0f);
        return min * t + (1.0f - t) * max;
    }

    public static int Range(int min, int max)          // RandomRangeInt
    {
        if (min < max) return (int)((uint)min + Next() % (uint)(max - min));
        if (max < min) return (int)((uint)min - Next() % (uint)(min - max));
        return min;
    }
}
