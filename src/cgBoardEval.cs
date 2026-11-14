// cgBoard.Evaluate and cgBoard.getBoardKey (Really Bad Chess 1.4.18), decompiled from libil2cpp.so.
using System;

public partial class cgBoard
{
    // @ 0x159a672
    // Material + piece-square tables, from white's point of view. Black pieces subtract the SAME
    // table entry (no vertical mirroring). Queens and kings have no positional term.
    public int Evaluate()
    {
        int value = 0;
        for (int i = 0; i < squares.Length; i++)
        {
            switch (squares[i])
            {
                case -6: value -= cgValueModifiers.Value_King; break;
                case -5: value -= cgValueModifiers.Value_Queen; break;
                case -4: value -= cgValueModifiers.Value_Bishop + cgValueModifiers.Positions_Bishop[i]; break;
                case -3: value -= cgValueModifiers.Value_Knight + cgValueModifiers.Positions_Knight[i]; break;
                case -2: value -= cgValueModifiers.Value_Rook + cgValueModifiers.Positions_Rook[i]; break;
                case -1: value -= cgValueModifiers.Value_Pawn + cgValueModifiers.Positions_Pawn[i]; break;
                case 1: value += cgValueModifiers.Value_Pawn + cgValueModifiers.Positions_Pawn[i]; break;
                case 2: value += cgValueModifiers.Value_Rook + cgValueModifiers.Positions_Rook[i]; break;
                case 3: value += cgValueModifiers.Value_Knight + cgValueModifiers.Positions_Knight[i]; break;
                case 4: value += cgValueModifiers.Value_Bishop + cgValueModifiers.Positions_Bishop[i]; break;
                case 5: value += cgValueModifiers.Value_Queen; break;
                case 6: value += cgValueModifiers.Value_King; break;
            }
        }
        if (_whiteKingMoves == 0 && _whiteARookMoves == 0 && !whiteHasCastled) value += cgValueModifiers.Value_CastlingLongRights;
        if (_blackKingMoves == 0 && _blackARookMoves == 0 && !blackHasCastled) value -= cgValueModifiers.Value_CastlingLongRights;
        if (_whiteKingMoves == 0 && _whiteHRookMoves == 0 && !whiteHasCastled) value += cgValueModifiers.Value_CastlingShortRights;
        if (_blackKingMoves == 0 && _blackHRookMoves == 0 && !blackHasCastled) value -= cgValueModifiers.Value_CastlingShortRights;
        if (blackHasCastled) value -= cgValueModifiers.Value_Castle;
        if (whiteHasCastled) value += cgValueModifiers.Value_Castle;
        return value;
    }

    // @ 0x159acc1
    // FNV-1 style hash of the 64 squares with a final avalanche. Side to move, castling and en
    // passant state are NOT part of the key.
    public int getBoardKey()
    {
        unchecked
        {
            const int p = 16777619;
            int hash = (int)2166136261;
            for (int i = 0; i < squares.Length; i++)
                hash = (hash ^ squares[i]) * p;
            hash += hash << 13;
            hash ^= hash >> 7;
            hash += hash << 3;
            hash ^= hash >> 17;
            hash += hash << 5;
            return hash;
        }
    }
}
