// Faithful decompilation (IL2CPP x86_64 -> C#) of the move-generation layer of Really Bad Chess.
// Source: W/cg.asm. Board index 0 = a8 ... 7 = h8, 56 = a1, 63 = h1.
using System;
using System.Collections.Generic;

// ============================================================================================
public class cgGlobal
{
    public static int pieceSet;
    public static string[] SquareNames;
    public static List<int> LeftDir;
    public static List<int> RightDir;
    public static List<int> TopDir;
    public static List<int> BotDir;
    public static List<int> RightBorder;
    public static List<int> LeftBorder;
    public static List<int> TopBorder;
    public static List<int> BotBorder;
    public static List<int> WhitePawnRow;
    public static List<int> BlackPawnRow;

    // @ 0x159e0ea
    static cgGlobal()
    {
        pieceSet = 3;
        // 65 entries: 64 squares in board-index order + sentinel at [64].
        SquareNames = new string[65] {
            "a8","b8","c8","d8","e8","f8","g8","h8",
            "a7","b7","c7","d7","e7","f7","g7","h7",
            "a6","b6","c6","d6","e6","f6","g6","h6",
            "a5","b5","c5","d5","e5","f5","g5","h5",
            "a4","b4","c4","d4","e4","f4","g4","h4",
            "a3","b3","c3","d3","e3","f3","g3","h3",
            "a2","b2","c2","d2","e2","f2","g2","h2",
            "a1","b1","c1","d1","e1","f1","g1","h1",
            "Out ouf bounds"   // [64] (typo is original)
        };
        LeftDir      = new List<int> { -9, -1, 7 };
        RightDir     = new List<int> { -7, 1, 9 };
        TopDir       = new List<int> { -9, -8, -7 };
        BotDir       = new List<int> { 7, 8, 9 };
        RightBorder  = new List<int> { 7, 15, 23, 31, 39, 47, 55, 63 };
        LeftBorder   = new List<int> { 0, 8, 16, 24, 32, 40, 48, 56 };
        TopBorder    = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
        BotBorder    = new List<int> { 56, 57, 58, 59, 60, 61, 62, 63 };
        WhitePawnRow = new List<int> { 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63 };
        BlackPawnRow = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        // NOTE: WhitePawnRow = ranks 2 AND 1, BlackPawnRow = ranks 8 AND 7 (Really Bad Chess can
        // place pawns on the back rank; both rows get the double step).
    }

    // @ 0x159d5c7
    public static string MoveToString(cgSimpleMove move)
    {
        if (move == null) return "";
        return SquareNames[move.from] + "->" + SquareNames[move.to];
    }

    // @ 0x159dd86
    public static string PosToString(int indexPos)
    {
        // NOTE: "> 0" (jle) means index 0 (a8) returns "no" — off-by-one bug in original.
        if (indexPos > 0 && indexPos < SquareNames.Length)
            return SquareNames[indexPos];
        return "no";
    }

    // @ 0x159de31
    public static byte IndexFromCellName(string p)
    {
        for (byte i = 0; i < SquareNames.Length; i++)
        {
            if (SquareNames[i] == p) return i;
        }
        return 0; // NOTE: not-found is indistinguishable from "a8"
    }
}

// ============================================================================================
public class cgValueModifiers
{
    public const int Value_Pawn = 1000;
    public const int Value_Rook = 5000;
    public const int Value_Knight = 3000;
    public const int Value_Bishop = 3050;
    public const int Value_Queen = 9000;
    public const int Value_King = 50000;
    public const short Value_CastlingShortRights = 22;
    public const short Value_CastlingLongRights = 15;
    public const sbyte Value_Castle = 85;
    public const sbyte AlphaBeta_Weight_Capture = 127;
    public const sbyte AlphaBeta_Weight_LongCastle = 30;
    public const sbyte AlphaBeta_Weight_ShortCastle = 30;
    public const sbyte AlphaBeta_Weight_Check = 50;
    public const sbyte AlphaBeta_Strong_Delineation = 20;
    // Declared but never referenced by any compiled code:
    private const int _ConnectedRooks = 30, _RookOnOpenFile = 60, _RookOnSemiOpenFile = 30,
        _KnightOutpost = 50, _BishopPawn = 20, _MultiplePawnFile = -25, _PawnChain = 30,
        _RookOppositeQueen = 50;
    public static sbyte[] Positions_Bishop;
    public static sbyte[] Positions_Rook;
    public static sbyte[] Positions_Knight;
    public static sbyte[] Positions_Pawn;
    public static sbyte[] Positions_Queen;

    // @ 0x159dc10
    static cgValueModifiers()
    {
        // Assignment order in the cctor, with the <PrivateImplementationDetails> field:
        Positions_Bishop = new sbyte[64] { /* F80A9B08... */
            -20,  0,  0,  0,  0,  0,  0,-20,
              0, 22,  5,  5,  5,  5, 22,  0,
             10,  0, 12, 15, 15, 12,  5, 10,
              5, 10, 12, 25, 25, 12, 10,  5,
              5, 10, 12, 25, 25, 12, 10,  5,
             10,  5, 12, 12, 15, 12,  5, 10,
              0, 22,  5,  5,  5,  5, 22,  0,
            -20,  0,  0,-10,  0,-10,  0,-20 };
        Positions_Rook = new sbyte[64] { /* F18B1630... */
              0,  5, 15, 18, 18, 15,  5,  0,
              0,  0,  0,  0,  0,  0,  0,  0,
              0,  0,  0,  0,  0,  0,  0,  0,
              0,  0,  0,  0,  0,  0,  0,  0,
              0,  0,  0,  0,  0,  0,  0,  0,
              0,  0,  0,  0,  0,  0,  0,  0,
              0,  0,  0,  0,  0,  0,  0,  0,
              0,  5,  7, 18, 18,  7,  5,  0 };
        Positions_Knight = new sbyte[64] { /* 778F558A... */
              0, -5,  0,  0,  0,  0, -5,  0,
              0,  0,  0,  5,  5,  0,  0,  0,
              0,  5, 25, 15, 15, 25,  5,  0,
              0,  5, 15, 30, 30, 15,  5,  0,
              0,  5, 15, 30, 30, 15,  5,  0,
              0,  5, 15, 15, 15, 15,  5,  0,
              0,  0,  0,  5,  5,  0,  0,  0,
              0, -5,  0,  0,  0,  0, -5,  0 };
        Positions_Pawn = new sbyte[64] { /* E7A0A28B... */
              0,  0,  0,  0,  0,  0,  0,  0,
              0,  0,  0,  0,  0,  0,  0,  0,
              3,  3,  5,  5,  5,  5,  3,  3,
              0,  3, 12, 15, 23, 12,  5,  0,
              0,  0, 15, 15, 22, 12,  7,  0,
              5,  5, 15, 15, 15, 15,  5,  5,
              0,  0,  0,  0,  0,  0,  0,  0,
              0,  0,  0,  0,  0,  0,  0,  0 };
        Positions_Queen = new sbyte[64] { /* 225FBC69... */
              0,  0,  0,  0,  1,  0,  0,  0,
              0,  2, 15,  0,  6,  0,  0,  0,
              3,  5,  0,  5,  5,  5,  3,  3,
             12,  0,  0,  0,  0,  0,  0, 15,
              0,  0,  0,  0,  0,  0,  0,  0,
              5,  5, 15, 15,  5,  5,  0,  0,
              0,  0, 12, 12,  5,  0,  0,  0,
              0,  0,  0,  0,  1,  0,  0,  0 };
        // NOTE: Positions_Queen is never read by cgMoveSet._findPositionalValueAt (see below).
    }
}

// ============================================================================================
public class cgSimpleMove
{
    public byte from;
    public byte to;
    public sbyte capturedType;
    public sbyte positionalVal;
    public int val;
    public cgSimpleMove bestResponse;
    public bool queened;

    // @ 0x15a15a4
    public cgSimpleMove(byte fromp, byte top, sbyte posVal)
    {
        from = fromp;
        to = top;
        positionalVal = posVal;
    }

    // @ 0x15a7657
    public cgSimpleMove(cgSimpleMove copyFrom)
    {
        from = copyFrom.from;
        to = copyFrom.to;
        capturedType = copyFrom.capturedType;
        positionalVal = copyFrom.positionalVal;
        val = copyFrom.val;
        queened = copyFrom.queened;
        // NOTE: bestResponse is NOT copied.
    }
}

public class cgCastlingMove : cgSimpleMove
{
    public byte secondFrom;
    public byte secondTo;

    // @ 0x15a155f   (base ctor inlined)
    public cgCastlingMove(byte fromp, byte top, sbyte posVal, byte s_from, byte s_to)
        : base(fromp, top, posVal)
    {
        secondFrom = s_from;
        secondTo = s_to;
    }
}

public class cgEnPassantMove : cgSimpleMove
{
    public byte attackingSquare;

    // @ 0x15a192e   (base ctor inlined)
    public cgEnPassantMove(byte fromp, byte tomp, sbyte posval, byte attackSquare)
        : base(fromp, tomp, posval)
    {
        attackingSquare = attackSquare;
    }
}

// ============================================================================================
public class cgMoveSet
{
    public byte from;
    public List<sbyte> moves;
    public sbyte type;
    public List<sbyte> positionalValues = new List<sbyte>();   // field initializer (runs before base ctor)

    // @ 0x15a1c40
    public cgMoveSet(List<int> movesp, int fromp, int typesp)
    {
        moves = new List<sbyte>();
        from = (byte)fromp;
        type = (sbyte)typesp;
        foreach (int m in movesp)
        {
            moves.Add((sbyte)m);                               // includes -1 / -2 separators
            positionalValues.Add(_findPositionalValueAt((sbyte)m)); // parallel list, same length
        }
    }

    // @ 0x15a74e2
    public sbyte _findPositionalValueAt(sbyte at)
    {
        if (at < 0) return 0;                       // separators get 0
        switch (type)                               // jump table over type+1 in [0..6]
        {
            case -1: // black pawn — uses WHITE pawn table, unmirrored
            case 1:
                return (sbyte)(cgValueModifiers.Positions_Pawn[at] - cgValueModifiers.Positions_Pawn[from]);
            case 2:
                return (sbyte)(cgValueModifiers.Positions_Rook[at] - cgValueModifiers.Positions_Rook[from]);
            case 3:
                return (sbyte)(cgValueModifiers.Positions_Knight[at] - cgValueModifiers.Positions_Knight[from]);
            case 4:
                return (sbyte)(cgValueModifiers.Positions_Bishop[at] - cgValueModifiers.Positions_Bishop[from]);
            case 5:
                // NOTE: queen uses Positions_Bishop and index 0 (constant), NOT 'at':
                //   asm @0x15a7608: rax=&Positions_Bishop[0], rcx=&Positions_Bishop[from]; al-cl.
                // => value = -20 - Positions_Bishop[from], independent of destination.
                return (sbyte)(cgValueModifiers.Positions_Bishop[0] - cgValueModifiers.Positions_Bishop[from]);
            case 0:
            default: // 6 (king) and all other negatives
                return 0;
        }
    }
}

// ============================================================================================
public class cgMoveGenerator
{
    // @ 0x15a2096
    public static List<int> EmulateBishopAt(int pos)
    {
        return FindMoves(new List<int> { -9, -7, 7, 9 }, pos);
    }

    // @ 0x15a1eea
    public static List<int> EmulateRookAt(int pos)
    {
        return FindMoves(new List<int> { -8, -1, 1, 8 }, pos);
    }

    // @ 0x15a1964
    public static List<int> EmulateQueenAt(int pos)
    {
        return FindMoves(new List<int> { -9, -8, -7, -1, 1, 7, 8, 9 }, pos);
    }

    // @ 0x15a33bc
    public static List<int> EmulateKingAt(int indexPosition)
    {
        List<int> moves = new List<int>();
        List<int> dirs = new List<int> { -9, -8, -7, -1, 1, 7, 8, 9 };
        foreach (int dir in dirs)
        {
            bool leftBlocked  = cgGlobal.LeftBorder.Contains(indexPosition)  && cgGlobal.LeftDir.Contains(dir);
            bool rightBlocked = cgGlobal.RightBorder.Contains(indexPosition) && cgGlobal.RightDir.Contains(dir);
            int target = indexPosition + dir;
            if (target >= 0 && target <= 63 && !leftBlocked && !rightBlocked)   // asm: unsigned (target <= 63)
                moves.Add(target);
        }
        return moves;   // no -1 separators for king
    }

    // @ 0x15a2242
    public static List<int> EmulateKnightAt(int indexPosition)
    {
        List<int> moves = new List<int>();
        List<int> p1 = new List<int> { -8, -8, -1 };
        List<int> p2 = new List<int> { -8, -8,  1 };
        List<int> p3 = new List<int> { -1, -1, -8 };
        List<int> p4 = new List<int> { -1, -1,  8 };
        List<int> p5 = new List<int> {  8,  8,  1 };
        List<int> p6 = new List<int> {  8,  8, -1 };
        List<int> p7 = new List<int> {  1,  1, -8 };
        List<int> p8 = new List<int> {  1,  1,  8 };
        List<List<int>> paths = new List<List<int>> { p1, p2, p3, p4, p5, p6, p7, p8 };

        foreach (List<int> path in paths)
        {
            bool leftBlocked  = cgGlobal.LeftBorder.Contains(indexPosition)  && cgGlobal.LeftDir.Contains(path[0]);
            bool rightBlocked = cgGlobal.RightBorder.Contains(indexPosition) && cgGlobal.RightDir.Contains(path[0]);
            // NOTE: range check here is on indexPosition itself (unsigned > 63), not on a target.
            if (rightBlocked || leftBlocked || (uint)indexPosition > 63) continue;

            int cur = indexPosition;
            for (int i = 0; i < path.Count + 1; i++)
            {
                if (i == path.Count)
                {
                    moves.Add(cur);
                    break;
                }
                bool l = cgGlobal.LeftBorder.Contains(cur)  && cgGlobal.LeftDir.Contains(path[i]);
                bool r = cgGlobal.RightBorder.Contains(cur) && cgGlobal.RightDir.Contains(path[i]);
                cur += path[i];
                if (r || l) break;
                if ((uint)cur > 63) break;
            }
        }
        return moves;   // no separators for knight
    }

    // @ 0x15a39b5
    public static List<int> EmulatePawnAt(int indexPosition, bool white)
    {
        List<int> moves = new List<int>();

        List<int> fwd = new List<int>();
        if (white) fwd.Add(-8); else fwd.Add(8);
        foreach (int dir in fwd)
        {
            bool leftBlocked  = cgGlobal.LeftBorder.Contains(indexPosition)  && cgGlobal.LeftDir.Contains(dir);
            bool rightBlocked = cgGlobal.RightBorder.Contains(indexPosition) && cgGlobal.RightDir.Contains(dir);
            int target = dir + indexPosition;
            if ((uint)target <= 63 && !leftBlocked && !rightBlocked)
                moves.Add(target);
        }

        if (white)
        {
            if (cgGlobal.WhitePawnRow.Contains(indexPosition))
            {
                moves.Add(indexPosition - 16);
                moves.Add(-1);
            }
        }
        else
        {
            if (cgGlobal.BlackPawnRow.Contains(indexPosition))
            {
                moves.Add(indexPosition + 16);
                moves.Add(-1);
            }
        }
        // NOTE: -1 is only appended after a double step; a single push has no -1 after it.
        // NOTE: double-step target is not range checked (e.g. black pawn on 15 -> 31 fine; all in range anyway).

        List<int> atk = new List<int>();
        if (white) { atk.Add(-7); atk.Add(-9); }
        else       { atk.Add(7);  atk.Add(9);  }

        moves.Add(-2);   // separator: pushes before, captures after

        foreach (int dir in atk)
        {
            bool leftBlocked  = cgGlobal.LeftBorder.Contains(indexPosition)  && cgGlobal.LeftDir.Contains(dir);
            bool rightBlocked = cgGlobal.RightBorder.Contains(indexPosition) && cgGlobal.RightDir.Contains(dir);
            int target = dir + indexPosition;
            if ((uint)target <= 63 && !leftBlocked && !rightBlocked)
                moves.Add(target);
        }
        return moves;
        // Layout: [push1?] [push2, -1]? -2 [cap(-7|+7)?] [cap(-9|+9)?]
    }

    // @ 0x15a6fba
    public static List<int> FindMoves(List<int> directions, int indexPosition)
    {
        List<int> moves = new List<int>();
        foreach (int dir in directions)
        {
            int pos = indexPosition;
            bool leftBlocked  = cgGlobal.LeftBorder.Contains(pos)  && cgGlobal.LeftDir.Contains(dir);
            bool rightBlocked = cgGlobal.RightBorder.Contains(pos) && cgGlobal.RightDir.Contains(dir);
            pos += dir;
            if (!rightBlocked && !leftBlocked && (uint)pos <= 63)
            {
                do
                {
                    moves.Add(pos);
                    bool l = cgGlobal.LeftBorder.Contains(pos)  && cgGlobal.LeftDir.Contains(dir);
                    bool r = cgGlobal.RightBorder.Contains(pos) && cgGlobal.RightDir.Contains(dir);
                    if (l || r) break;
                    pos += dir;
                } while ((uint)pos < 64);
            }
            moves.Add(-1);   // ray terminator after EVERY direction (even empty rays)
        }
        return moves;
    }
}

// ============================================================================================
public partial class cgBoard
{
    private static cgMoveSet[] _moveSetCache;   // PORT: see _findMoveSetFor

    // @ 0x15a035a   (called at the end of every loadBoard(); rebuilds the whole dictionary)
    public void _generateAllPossibleMoves()
    {
        allHypotheticalMoves = new Dictionary<string, cgMoveSet>();
        for (int i = 0; i < 64; i++)
        {
            allHypotheticalMoves["5"  + i.ToString()] = new cgMoveSet(cgMoveGenerator.EmulateQueenAt(i),  i, 5);
            allHypotheticalMoves["2"  + i.ToString()] = new cgMoveSet(cgMoveGenerator.EmulateRookAt(i),   i, 2);
            allHypotheticalMoves["4"  + i.ToString()] = new cgMoveSet(cgMoveGenerator.EmulateBishopAt(i), i, 4);
            allHypotheticalMoves["3"  + i.ToString()] = new cgMoveSet(cgMoveGenerator.EmulateKnightAt(i), i, 3);
            allHypotheticalMoves["6"  + i.ToString()] = new cgMoveSet(cgMoveGenerator.EmulateKingAt(i),   i, 6);
            allHypotheticalMoves["1"  + i.ToString()] = new cgMoveSet(cgMoveGenerator.EmulatePawnAt(i, true),  i, 1);
            allHypotheticalMoves["-1" + i.ToString()] = new cgMoveSet(cgMoveGenerator.EmulatePawnAt(i, false), i, -1);
        }
        _moveSetCache = new cgMoveSet[8 * 64];
        foreach (int t in new[] { -1, 1, 2, 3, 4, 5, 6 })
            for (int i = 0; i < 64; i++)
                _moveSetCache[(t + 1) * 64 + i] = allHypotheticalMoves[t.ToString() + i.ToString()];
    }

    // @ 0x15a096f
    public cgMoveSet _findMoveSetFor(int piece, int indexPosition)
    {
        if (piece < -1) piece = Math.Abs(piece);    // black non-pawns share white movesets
        // PORT: the original looks up allHypotheticalMoves[piece.ToString() + indexPosition.ToString()].
        // The same objects are cached in an array (rebuilt whenever the dictionary is) because the
        // string key dominated the search's run time. Results are identical.
        if (piece >= -1 && piece <= 6 && piece != 0 && (uint)indexPosition < 64)
            return _moveSetCache[(piece + 1) * 64 + indexPosition];
        return allHypotheticalMoves[piece.ToString() + indexPosition.ToString()];
        // piece == 0 -> KeyNotFoundException
    }
}
