// cgEngine: the chess AI of Really Bad Chess 1.4.18, decompiled from libil2cpp.so (x86_64).
//
// The original is a Unity MonoBehaviour that runs the search on a System.Threading.Thread and
// animates a "thinking" load bar from Update(). This port keeps the search itself unchanged and
// runs it synchronously; the Unity-only parts (load bar, sound cue selection, thread handling)
// are described in comments where they were.
using System;
using System.Collections.Generic;

namespace ReallyBadChess
{
    public class cgEngine
    {
        public bool updateLoadBar = true;          // 0x20
        public bool MoveAsWhite = true;            // 0x21
        public bool Finished;                      // 0x22
        public byte SearchDepthWeak = 4;           // 0x23
        public byte SearchDepthStrong = 4;         // 0x24
        public byte SearchDepthEndGame = 5;        // 0x25
        public int BoardStatesExamined;            // 0x28 (never updated by the search)
        public int CurrentBoardValue;              // 0x2c
        public List<cgSimpleMove> Moves;           // 0x30
        public volatile int MovesLeftToAnalyze;    // 0x38
        public int TotalMovesToAnalyze;            // 0x3c
        public volatile bool paused;               // 0x40
        public volatile bool paused2;              // 0x41
        public volatile bool processingComplete;   // 0x42
        private cgBoard _board;                    // 0x68
        private cgBoard _board_ref;                // 0x70
        private Action<cgSimpleMove> _callback;    // 0x78
        private Dictionary<int, int> prevStateValues; // 0x88

        /// <summary>
        /// Synchronous convenience wrapper: search <paramref name="board"/> for the side given by
        /// <paramref name="asWhite"/> and return the chosen move (null if there is none).
        /// </summary>
        public cgSimpleMove FindMove(cgBoard board, bool asWhite)
        {
            cgSimpleMove result = null;
            MakeMove(board, asWhite, m => result = m);
            return result;
        }

        // @ 0x159961b
        public void MakeMove(cgBoard board, bool MoveAsWhiteP, Action<cgSimpleMove> callback)
        {
            paused = false;
            Finished = false;
            _board = new cgBoard(null);
            board.copyTo(_board);
            _board_ref = board;
            MoveAsWhite = MoveAsWhiteP;
            _callback = callback;
            _startAnalysisThreadded();
        }

        // @ 0x15999ea
        private void _startAnalysisThreadded()
        {
            processingComplete = false;
            // Original: aborts a still-running processingThread and creates a new
            // Thread(_processThreadded); Finished = false.
            Finished = false;
            Moves = _board.findStrictLegalMoves(MoveAsWhite, false);
            if (Moves.Count <= 0)
                return;                     // no thread is started and the callback never fires
            MovesLeftToAnalyze = Moves.Count;
            for (int i = 0; i < Moves.Count; i++)
            {
                byte depth = Moves[i].positionalVal > cgValueModifiers.AlphaBeta_Strong_Delineation
                    ? SearchDepthStrong : SearchDepthWeak;
                if (depth == SearchDepthStrong)
                    MovesLeftToAnalyze += 10;   // progress-bar weight of a deep search
            }
            TotalMovesToAnalyze = MovesLeftToAnalyze;
            // Original (Unity only): if manager's sound flag (+0x158) is set, picks a music/thinking
            // cue on manager+0x150 via a virtual call: "e" if the opponent's last move captured a
            // rook or bishop (capturedType -2 or -5 per the board's move list), else by
            // TotalMovesToAnalyze: >=101 "b", >=81 "a", >=66 "c", otherwise "d". Then resets the
            // load bar scale and starts the thread. Here the thread body runs inline:
            _processThreadded();
            _analysisComplete();            // original: called from Update() once processingComplete
        }

        // @ 0x1599f0c
        private void _processThreadded()
        {
            prevStateValues = new Dictionary<int, int>();
            for (int i = Moves.Count - 1; i >= 0; i--)
            {
                if (paused || paused2)
                {
                    i++;                    // original busy-waits on the same move while paused
                    continue;
                }
                cgSimpleMove move = Moves[i];
                byte depth;
                if (Moves.Count <= 9)
                    depth = SearchDepthEndGame;
                else
                    depth = move.positionalVal > cgValueModifiers.AlphaBeta_Strong_Delineation
                        ? SearchDepthStrong : SearchDepthWeak;
                move.val = _alfaBeta(move, depth, int.MinValue, int.MaxValue, !MoveAsWhite);
                if (depth == SearchDepthStrong)
                    MovesLeftToAnalyze -= 10;
                MovesLeftToAnalyze--;
            }
            _sortMovesOnBoardValue(Moves, MoveAsWhite);
            // Debug.Log("left: " + MovesLeftToAnalyze);
            processingComplete = true;
        }

        // @ 0x159a5dc
        private void _analysisComplete()
        {
            _sortMovesOnBoardValue(Moves, MoveAsWhite);
            if (Moves.Count > 0)
                _callback(Moves[0]);
            CurrentBoardValue = _board.Evaluate();
            Finished = true;
        }

        // @ 0x159a10c
        // Plain fail-hard minimax with alpha-beta. White always maximises. Positions are memoised
        // in prevStateValues by board hash only (not depth, side to move, or window), for the whole
        // root search: the first value stored for a position is reused at any depth.
        private int _alfaBeta(cgSimpleMove node, int depth, int alpha, int beta, bool maximizing)
        {
            _board.move(node);
            int key = _board.getBoardKey();
            int value;
            if (prevStateValues.ContainsKey(key))
            {
                value = prevStateValues[key];
            }
            else if (depth == 0)
            {
                value = _board.Evaluate();      // leaf values are not memoised
            }
            else
            {
                if (maximizing)
                {
                    // NOTE: only the maximising (white) side switches to fully legal move
                    // generation when 16 or fewer pieces remain; black always uses pseudo-legal.
                    List<cgSimpleMove> moves = _board.remainingPieces > 16
                        ? _board.findLegalMoves(true, false, false)
                        : _board.findStrictLegalMoves(true, false);
                    depth--;
                    value = int.MinValue;
                    foreach (cgSimpleMove child in moves)
                    {
                        value = Math.Max(value, _alfaBeta(child, depth, alpha, beta, false));
                        alpha = Math.Max(alpha, value);
                        if (alpha > beta) break;
                    }
                }
                else
                {
                    List<cgSimpleMove> moves = _board.findLegalMoves(false, false, false);
                    depth--;
                    value = int.MaxValue;
                    foreach (cgSimpleMove child in moves)
                    {
                        value = Math.Min(value, _alfaBeta(child, depth, alpha, beta, true));
                        beta = Math.Min(beta, value);
                        if (beta < alpha) break;
                    }
                }
                if (!prevStateValues.ContainsKey(key))
                    prevStateValues.Add(key, value);
            }
            _board.revert();
            return value;
        }

        // @ 0x159a4ba
        private void _sortMovesOnBoardValue(List<cgSimpleMove> moves, bool white)
        {
            // List<T>.Sort is an unstable introsort; the port uses the same BCL algorithm.
            moves.Sort((x, y) => x.val.CompareTo(y.val));   // cgEngine.<>c.<_sortMovesOnBoardValue>b__31_0
            if (white)
                moves.Reverse();
        }

        // Original also has cancel() (sets Finished, aborts the thread, hides the load bar),
        // Update() (load-bar animation; calls _analysisComplete() once processingComplete),
        // TellLegals/_testBoard/_debug/_debugReadBoard (debug logging only).
    }
}
