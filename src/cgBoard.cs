// cgBoard — faithful decompilation of selected methods (from W/cg.asm, IL2CPP x86_64).
// Field layout (dump_acs.cs):
//   sbyte[] startingBoardState @0x10, sbyte[] squares @0x18, List<cgSimpleMove> moves @0x20,
//   int moveCount @0x28, int revertCount @0x2c, bool whiteHasCastled @0x30, bool blackHasCastled @0x31,
//   byte _enPassantSquare @0x32, byte _enPassantCapturesOn @0x33, bool _whiteTurnToMove @0x34,
//   byte _whiteARookMoves @0x35, _whiteHRookMoves @0x36, _blackARookMoves @0x37, _blackHRookMoves @0x38,
//   byte _whiteKingMoves @0x39, _blackKingMoves @0x3a, byte remainingPieces @0x3b,
//   int seed @0x3c, targetForGeneration @0x40, currentGameSpread @0x44, currentGameProxToSpread @0x48
//   static Dictionary<string,cgMoveSet> allHypotheticalMoves (static +0x0), static sbyte[] defaultStartPosition (+0x8)
//
// Square 65 (0x41) is used as a "no square"/null-move sentinel for cgSimpleMove.to.

using System;
using System.Collections.Generic;
using Vector2 = System.Numerics.Vector2;

public partial class cgBoard
{
    public sbyte[] startingBoardState = new sbyte[64];
    public sbyte[] squares = new sbyte[64];
    public List<cgSimpleMove> moves = new List<cgSimpleMove>();
    public int moveCount;
    public int revertCount;
    public bool whiteHasCastled;
    public bool blackHasCastled;
    public byte _enPassantSquare;        // set by findLegalMoves (reset to 65), read by removeIllegalMoves; NOT touched by move/revert
    public byte _enPassantCapturesOn;    // same as above
    public bool _whiteTurnToMove = true;
    public byte _whiteARookMoves;
    public byte _whiteHRookMoves;
    public byte _blackARookMoves;
    public byte _blackHRookMoves;
    public byte _whiteKingMoves;
    public byte _blackKingMoves;
    public byte remainingPieces = 32;
    private static Dictionary<string, cgMoveSet> allHypotheticalMoves;   // = null in .cctor
    public static sbyte[] defaultStartPosition = new sbyte[64] {         // <PID>.5AB889CB...
        -2, -3, -4, -5, -6, -4, -3, -2,
        -1, -1, -1, -1, -1, -1, -1, -1,
         0,  0,  0,  0,  0,  0,  0,  0,
         0,  0,  0,  0,  0,  0,  0,  0,
         0,  0,  0,  0,  0,  0,  0,  0,
         0,  0,  0,  0,  0,  0,  0,  0,
         1,  1,  1,  1,  1,  1,  1,  1,
         2,  3,  4,  5,  6,  4,  3,  2 };
    public int seed = -1;
    public int targetForGeneration = -99;
    public int currentGameSpread = -99;
    public int currentGameProxToSpread = -99;

    // @ 0x15a0356
    public bool whiteTurnToMove { get { return _whiteTurnToMove; } }

    // @ 0x1599909
    // Copies squares/startingBoardState and the scalar state fields. Does NOT copy `moves`,
    // nor seed/targetForGeneration/currentGameSpread/currentGameProxToSpread.
    public void copyTo(cgBoard target)
    {
        for (int i = 0; i < 64; i++)
        {
            target.startingBoardState[i] = startingBoardState[i];
            target.squares[i] = squares[i];
        }
        target.moveCount = moveCount;
        target.revertCount = revertCount;
        target.whiteHasCastled = whiteHasCastled;
        target.blackHasCastled = blackHasCastled;
        target._enPassantSquare = _enPassantSquare;
        target._enPassantCapturesOn = _enPassantCapturesOn;
        target._whiteTurnToMove = _whiteTurnToMove;
        target._whiteARookMoves = _whiteARookMoves;
        target._whiteHRookMoves = _whiteHRookMoves;
        target._blackARookMoves = _blackARookMoves;
        target._blackHRookMoves = _blackHRookMoves;
        target._whiteKingMoves = _whiteKingMoves;
        target._blackKingMoves = _blackKingMoves;
        target.remainingPieces = remainingPieces;
    }

    // @ 0x15996a9
    // (field initializers above run first: two new sbyte[64], new List, _whiteTurnToMove=true,
    //  remainingPieces=32, seed=-1, the three others=-99; then base Object ctor)
    public cgBoard(List<sbyte> positions)
    {
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
        if (allHypotheticalMoves == null)
            _generateAllPossibleMoves();
        // NOTE: remainingPieces is always initialised to 32 regardless of the supplied position.
    }

    // @ 0x15a0859
    // NOTE: unlike the ctor, _generateAllPossibleMoves() is called unconditionally (rebuilds the static table),
    // and no other state (turn, counters, moves, remainingPieces) is reset.
    public void loadBoard(List<sbyte> positions)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            squares[i] = positions[i];
            startingBoardState[i] = positions[i];
        }
        _generateAllPossibleMoves();
    }

    // @ 0x159a9bf
    public void move(cgSimpleMove move)
    {
        moves.Add(move);
        moveCount++;
        if (move.to == 65)
            return;                     // null move: recorded, but turn is NOT flipped

        move.capturedType = squares[move.to];
        if (move.capturedType != 0)
            remainingPieces--;
        squares[move.to] = squares[move.from];
        squares[move.from] = 0;

        if (move.to <= 7)
        {
            if (squares[move.to] == 1)
            {
                move.queened = true;
                squares[move.to] = 5;
            }
        }
        else if (move.to >= 56)
        {
            if (squares[move.to] == -1)
            {
                move.queened = true;
                squares[move.to] = -5;
            }
        }

        // Type tests are IL2CPP IsInstClass (typeHierarchy[depth-1] == klass), i.e. C# `is` / `as`.
        if (move is cgCastlingMove)
        {
            squares[(move as cgCastlingMove).secondTo] = squares[(move as cgCastlingMove).secondFrom];
            squares[(move as cgCastlingMove).secondFrom] = 0;
            if (_whiteTurnToMove) whiteHasCastled = true;
            else blackHasCastled = true;
        }
        else if (move is cgEnPassantMove)
        {
            move.capturedType = squares[(move as cgEnPassantMove).attackingSquare];
            squares[(move as cgEnPassantMove).attackingSquare] = 0;
            // NOTE: remainingPieces is NOT decremented for an en-passant capture (but revert increments it).
        }

        // Rook/king move counters, keyed on the piece now standing on `to` and the origin square.
        if (squares[move.to] == 2)
        {
            if (move.from == 63) _whiteHRookMoves++;
            else if (move.from == 56) _whiteARookMoves++;
        }
        else if (squares[move.to] == -2)
        {
            if (move.from == 7) _blackHRookMoves++;
            else if (move.from == 0) _blackARookMoves++;
        }
        if (squares[move.to] == -6) _blackKingMoves++;
        else if (squares[move.to] == 6) _whiteKingMoves++;
        // NOTE: the castling rook (secondFrom->secondTo) does not bump a rook counter; capturing a rook
        // on its home square doesn't either (castling with a "phantom" rook prevented only by the
        // square contents check in move generation).

        _whiteTurnToMove = !_whiteTurnToMove;
    }

    // @ 0x159ad33
    // Returns a material score of the piece captured by the reverted move — BLACK pieces only:
    // pawn 1, knight/bishop 3, rook 5, queen 9; white captures / no capture / king -> 0. Null move -> 0.
    public int revert()
    {
        // NOTE: evaluated before the Count>0 check -> ArgumentOutOfRangeException on an empty list.
        int capturedVal;
        if (moves[moves.Count - 1].capturedType != 0)
        {
            remainingPieces++;
            if (moves[moves.Count - 1].capturedType == -1) capturedVal = 1;
            else if (moves[moves.Count - 1].capturedType == -4) capturedVal = 3;
            else if (moves[moves.Count - 1].capturedType == -3) capturedVal = 3;
            else if (moves[moves.Count - 1].capturedType == -2) capturedVal = 5;
            else capturedVal = (moves[moves.Count - 1].capturedType == -5) ? 9 : 0;
        }
        else
        {
            capturedVal = 0;
        }
        // NOTE: for a null move (to==65) capturedType is never written by move(), so a stale/initial
        // capturedType on the object would still bump remainingPieces here.

        if (moves.Count > 0)
        {
            revertCount++;
            if (moves[moves.Count - 1].to == 65)
            {
                moves.RemoveAt(moves.Count - 1);
                return 0;               // turn not flipped back (matches move())
            }

            squares[moves[moves.Count - 1].from] = squares[moves[moves.Count - 1].to];
            if ((moves[moves.Count - 1] as cgEnPassantMove) != null)
            {
                squares[(moves[moves.Count - 1] as cgEnPassantMove).attackingSquare] =
                    (moves[moves.Count - 1] as cgEnPassantMove).capturedType;
                squares[(moves[moves.Count - 1] as cgEnPassantMove).to] = 0;
            }
            else
            {
                squares[moves[moves.Count - 1].to] = moves[moves.Count - 1].capturedType;
            }

            if (moves[moves.Count - 1].queened)
                squares[moves[moves.Count - 1].from] = (sbyte)((moves[moves.Count - 1].from < 31) ? 1 : -1);

            if (squares[moves[moves.Count - 1].from] == -2 && moves[moves.Count - 1].from == 0) _blackARookMoves--;
            else if (squares[moves[moves.Count - 1].from] == -2 && moves[moves.Count - 1].from == 7) _blackHRookMoves--;
            else if (squares[moves[moves.Count - 1].from] == 2 && moves[moves.Count - 1].from == 56) _whiteARookMoves--;
            else if (squares[moves[moves.Count - 1].from] == 2 && moves[moves.Count - 1].from == 63) _whiteHRookMoves--;

            if (squares[moves[moves.Count - 1].from] == 6) _whiteKingMoves--;
            else if (squares[moves[moves.Count - 1].from] == -6) _blackKingMoves--;

            if (moves[moves.Count - 1] is cgCastlingMove)
            {
                squares[(moves[moves.Count - 1] as cgCastlingMove).secondFrom] =
                    squares[(moves[moves.Count - 1] as cgCastlingMove).secondTo];
                squares[(moves[moves.Count - 1] as cgCastlingMove).secondTo] = 0;
                // turn not yet flipped back: side to move now is the opponent of the castler
                if (_whiteTurnToMove) blackHasCastled = false;
                else whiteHasCastled = false;
            }

            moves.RemoveAt(moves.Count - 1);
            _whiteTurnToMove = !_whiteTurnToMove;
        }
        // NOTE: moveCount is never decremented (monotonic count of move() calls); revertCount counts reverts.
        return capturedVal;
    }

    // @ 0x15a0913
    public bool longCastlingRights(bool white)
    {
        if (white)
            return _whiteKingMoves == 0 && _whiteARookMoves == 0 && !whiteHasCastled;
        return _blackKingMoves == 0 && _blackARookMoves == 0 && !blackHasCastled;
    }

    // @ 0x15a0941
    public bool shortCastlingRights(bool white)
    {
        if (white)
            return _whiteKingMoves == 0 && _whiteHRookMoves == 0 && !whiteHasCastled;
        return _blackKingMoves == 0 && _blackHRookMoves == 0 && !blackHasCastled;
    }

    // @ 0x15a1751
    // y = row from the top (0 = rank 8), x = column (0 = a-file).
    public Vector2 toXY(byte pos)
    {
        int y = Convert.ToInt32(pos) / 8;
        return new Vector2((float)pos - 8f * y, (float)y);   // compiled as pos + (-8.0f * y)
    }

    // @ 0x15a4316
    // LoadGame(cgNotation notation) @ 0x15a4316: foreach (m in notation.moves) move(m); (cgNotation not ported)

    // @ 0x15a444e
    // True if any pseudo-legal move of the opponent lands on a king (either colour: Math.Abs).
    public bool isChecked(bool asWhite)
    {
        foreach (cgSimpleMove m in findLegalMoves(!asWhite, false, false))
        {
            if (m.to == 65) continue;
            if (Math.Abs(squares[m.to]) == 6)
                return true;
        }
        return false;
    }

    // @ 0x159d75f
    // Debug.Log of the board: for each i < squares.Length, "" + " [" + (squares[i]==0 ? " " : "") ... squares[i] + "] "
    // with "\n" every 8 squares, then " En passant at:" + _enPassantSquare + " white has castled:" + whiteHasCastled
    // + " black has castled:" + blackHasCastled + " black has long cast rights:" + longCastlingRights(false)(inlined)
    // + " black has short castling rights." + shortCastlingRights(false)(inlined) + squares.Length + ", count."
    public void debugReadBoard() { /* debug only — see summary above */ }
}
