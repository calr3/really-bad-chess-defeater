// Faithful decompilation of cgBoard legal-move generation (Really Bad Chess, IL2CPP x86_64).
// Source: W/cg.asm. Inlined ctors (cgSimpleMove / cgCastlingMove / cgEnPassantMove only store fields)
// are written as normal constructor calls. capturedType is never set at generation time (stays 0).
//
// Conventions recap: squares[0]=a8 .. squares[63]=h1. White moves toward index 0.
// Square 65 is the "null move" marker (cgSimpleMove(65,65,0)).
// cgMoveSet.moves: target squares; -1 = end of a ray/direction; -2 = (pawns) switch from
// forward pushes to diagonal-capture squares. cgMoveSet.type is the ABSOLUTE piece type for
// everything except pawns (black pawns = -1), because _findMoveSetFor abs()es piece <= -2.

using System;
using System.Collections.Generic;

public partial class cgBoard
{
    // @ 0x159b6eb
    // NOTE: doAdvancedPinCheck is never read (the register holding it is not saved or used).
    public List<cgSimpleMove> findLegalMoves(bool asWhite, bool disregardCheckChecks, bool doAdvancedPinCheck)
    {
        List<cgSimpleMove> legalMoves = new List<cgSimpleMove>();   // [rsp+0x20]
        List<cgSimpleMove> enemyMoves = new List<cgSimpleMove>();   // [rsp+0x50]

        // ---- en passant square from the last move ----
        _enPassantSquare = 65;
        _enPassantCapturesOn = 65;
        if (moves.Count > 0
            && moves[moves.Count - 1].to != 65
            && Math.Abs(squares[moves[moves.Count - 1].to]) == 1
            && Math.Abs(moves[moves.Count - 1].from - moves[moves.Count - 1].to) == 16)
        {
            // byte arithmetic: from + (to < 32 ? 16 : 0) - 8   (square jumped over)
            _enPassantSquare = (byte)(moves[moves.Count - 1].from + (moves[moves.Count - 1].to < 32 ? 16 : 0) - 8);
            _enPassantCapturesOn = moves[moves.Count - 1].to;
        }
        // NOTE: the ep square is set regardless of which side moved; removeIllegalMoves lets ANY pawn
        // in capture mode whose diagonal target equals it play an ep capture (only matters for enemyMoves).

        // ---- pseudo-legal generation, board order 0..63 ----
        for (int i = 0; i < squares.Length; i++)
        {
            sbyte p = squares[i];
            if ((asWhite && p > 0) || (!asWhite && p < 0))
            {
                cgMoveSet set = _findMoveSetFor(p, i);
                if (set != null)
                    legalMoves.AddRange(removeIllegalMoves(set));
            }
            else if (p != 0)
            {
                enemyMoves.AddRange(removeIllegalMoves(_findMoveSetFor(p, i)));
            }
        }

        // ---- castling rights (rook "type 2" on its home square; path squares empty) ----
        // Compiler reordered these tests; the result is equivalent to the following.
        bool castleSideWhite = asWhite;   // [rsp+0xc]; always == asWhite in practice
        bool canShort = false;            // r15  (h-rook side)
        bool canLong = false;             // ebp  (a-rook side)
        if (asWhite)
        {
            if (_whiteKingMoves == 0 && !whiteHasCastled && !(_whiteHRookMoves != 0 && _whiteARookMoves != 0))
            {
                canShort = _whiteHRookMoves == 0 && squares[63] == 2;
                canLong  = _whiteARookMoves == 0 && squares[56] == 2;
                if (canShort && asWhite)
                    canShort = squares[61] == 0 && squares[62] == 0;
                castleSideWhite = true;
                if (canLong && asWhite)
                    canLong = squares[58] == 0 && squares[59] == 0 && squares[57] == 0;
            }
        }
        else
        {
            if (_blackKingMoves == 0 && !blackHasCastled && !(_blackHRookMoves != 0 && _blackARookMoves != 0))
            {
                canShort = _blackHRookMoves == 0 && squares[7] == -2;
                canLong  = _blackARookMoves == 0 && squares[0] == -2;
                if (canShort && !asWhite)
                    canShort = squares[5] == 0 && squares[6] == 0;
                castleSideWhite = false;
                if (canLong && !asWhite)
                    canLong = squares[2] == 0 && squares[3] == 0 && squares[1] == 0;
            }
        }
        // NOTE: the king's presence on e1/e8 is never checked, only the move counters.

        // ---- scan enemy moves: check detection, pins, castling-through-attack ----
        bool notInCheck = true;   // [rsp+0x4c]
        foreach (cgSimpleMove em in enemyMoves)
        {
            if (Math.Abs(squares[em.to]) == 6)
            {
                // our king is attacked
                canLong = false;
                canShort = false;
                notInCheck = false;
                continue;
            }

            if (squares[em.to] != 0)   // enemy capture of one of our pieces -> pin test
            {
                List<byte> ray = _getFullRayFor(_findMoveSetFor(squares[em.from], em.from), em.to);
                if (ray != null && ray.Count > 0)
                {
                    sbyte kingIdx = -1;
                    sbyte blockers = 0;          // [rsp+0x58], byte-sized
                    for (byte i = 0; i < ray.Count; i++)
                    {
                        if ((squares[ray[i]] == 6 && squares[em.from] < 0) ||
                            (squares[ray[i]] == -6 && squares[em.from] > 0))
                        {
                            kingIdx = (sbyte)i;
                            break;
                        }
                        if (ray[i] != em.from)
                        {
                            if (squares[ray[i]] != 0)
                                blockers++;
                        }
                    }
                    if (kingIdx != -1 && blockers == 1)
                    {
                        // keep only attacker-side part of the ray (squares before the king)
                        ray.RemoveRange(kingIdx, ray.Count - kingIdx);
                        if (!disregardCheckChecks)
                        {
                            // NOTE: counter is a byte cast of legalMoves.Count (breaks if Count >= 256).
                            for (byte j = (byte)legalMoves.Count; j != 0; )
                            {
                                j--;
                                if (ray.Contains(legalMoves[j].from)
                                    && !ray.Contains(legalMoves[j].to)
                                    && legalMoves[j].to != em.from)
                                {
                                    legalMoves.RemoveAt(j);
                                }
                            }
                        }
                    }
                }
            }

            // castling through attacked squares (enemy move destinations only)
            if (canShort)
            {
                if (asWhite) { if (em.to == 61 || em.to == 62) canShort = false; }
                else         { if (em.to == 5  || em.to == 6)  canShort = false; }
            }
            if (canLong)
            {
                if (asWhite) { if ((em.to & 0xFE) == 58) canLong = false; }   // 58,59
                else         { if ((em.to & 0xFE) == 2)  canLong = false; }   // 2,3
            }
        }
        // NOTE: enemyMoves contains no pawn diagonal "attacks" on empty squares and no
        // "defends" of their own pieces, so: castling past a pawn-attacked f1/g1/d1/c1 is allowed,
        // and the king may capture a defended piece (only findStrictLegalMoves/verifyLegality catches that).

        // ---- castling moves ----
        if (castleSideWhite && canLong)
            legalMoves.Add(new cgCastlingMove(60, 58, cgValueModifiers.AlphaBeta_Weight_LongCastle, 56, 59));
        else if (canLong && !asWhite)
            legalMoves.Add(new cgCastlingMove(4, 2, cgValueModifiers.AlphaBeta_Weight_LongCastle, 0, 3));

        if (castleSideWhite && canShort)
            legalMoves.Add(new cgCastlingMove(60, 62, cgValueModifiers.AlphaBeta_Weight_ShortCastle, 63, 61));
        else if (canShort && !asWhite)
            legalMoves.Add(new cgCastlingMove(4, 6, cgValueModifiers.AlphaBeta_Weight_ShortCastle, 7, 5));

        // ---- ordering: ascending by positionalVal, then reversed => descending ----
        // NOTE: List.Sort is the .NET (IL2CPP corlib) introsort -> unstable; ties end up in the
        // order introsort leaves them, then the whole list is reversed.
        legalMoves.Sort((x, y) => x.positionalVal.CompareTo(y.positionalVal));   // <>c.<findLegalMoves>b__37_0
        legalMoves.Reverse();

        // ---- king may not step onto an enemy move destination ----
        if (!disregardCheckChecks)
        {
            for (short i = (short)(legalMoves.Count - 1); i >= 0; i--)
            {
                if (Math.Abs(squares[legalMoves[i].from]) == 6)
                {
                    foreach (cgSimpleMove em in enemyMoves)
                    {
                        if (em.to == legalMoves[i].to)
                        {
                            if (Math.Abs(squares[em.from]) == 1)
                                continue;      // pawn moves ignored (forward pushes aren't attacks)
                            legalMoves.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }

        if (legalMoves.Count == 0)
            legalMoves.Add(new cgSimpleMove(65, 65, 0));

        if (notInCheck)
            return legalMoves;

        // ---- we are in check ----
        if (!disregardCheckChecks)
        {
            List<cgSimpleMove> checkers = new List<cgSimpleMove>();
            foreach (cgSimpleMove em in enemyMoves)
            {
                if (Math.Abs(squares[em.to]) == 6)
                    checkers.Add(em);
            }

            if (checkers.Count == 1)
            {
                List<byte> blockSquares = _getBlockAttackSquares(checkers[0]);
                for (int i = legalMoves.Count - 1; i >= 0; i--)
                {
                    if (legalMoves[i].to == 65) continue;
                    if (blockSquares.Contains(legalMoves[i].to)) continue;
                    if (Math.Abs(squares[legalMoves[i].from]) == 6) continue;
                    legalMoves.RemoveAt(i);
                }
                // NOTE: an en-passant capture of a checking pawn (to = ep square) is removed.
            }
            else
            {
                // double check: only king moves (and the null move, from == 65) survive
                for (int i = legalMoves.Count - 1; i >= 0; i--)
                {
                    if (legalMoves[i].from > 64) continue;
                    if (Math.Abs(squares[legalMoves[i].from]) == 6) continue;
                    legalMoves.RemoveAt(i);
                }
            }
        }

        if (legalMoves.Count == 0)
            legalMoves.Add(new cgSimpleMove(65, 65, 0));
        return legalMoves;
    }

    // @ 0x15a0c6a
    // Turns a precomputed cgMoveSet into pseudo-legal moves against the current board.
    public List<cgSimpleMove> removeIllegalMoves(cgMoveSet moveSet)
    {
        List<cgSimpleMove> list = new List<cgSimpleMove>();
        sbyte piece = squares[moveSet.from];
        bool canCapture = !(moveSet.type == 1 || moveSet.type == -1);  // pawns start in push mode
        bool isWhite = piece > 0;
        bool rayOpen = true;
        bool canMoveToEmpty = true;

        for (int i = 0; i < moveSet.moves.Count; i++)
        {
            if (moveSet.type == 6 || moveSet.type == 3)
                rayOpen = true;                     // king / knight: every target independent

            if (moveSet.moves[i] == -1) { rayOpen = true; continue; }
            if (moveSet.moves[i] == -2) { canCapture = true; canMoveToEmpty = false; continue; }

            if (canMoveToEmpty && !rayOpen) continue;

            if (squares[moveSet.moves[i]] == 0 && canMoveToEmpty)
            {
                list.Add(new cgSimpleMove(moveSet.from, (byte)moveSet.moves[i], moveSet.positionalValues[i]));
                continue;
            }

            sbyte target = squares[moveSet.moves[i]];
            bool enemy = (piece <= 0 && target > 0) || (isWhite && target < 0);
            if (enemy)
            {
                if (!canCapture) { rayOpen = false; continue; }   // pawn push blocked
                list.Add(new cgSimpleMove(moveSet.from, (byte)moveSet.moves[i], cgValueModifiers.AlphaBeta_Weight_Capture));
                if (Math.Abs(squares[moveSet.moves[i]]) == 6)
                    list[list.Count - 1].positionalVal = cgValueModifiers.AlphaBeta_Weight_Check;  // king "capture" = 50
                rayOpen = false;
                continue;
            }

            if (squares[moveSet.moves[i]] == 0 && canCapture && !canMoveToEmpty
                && _enPassantSquare == moveSet.moves[i])
            {
                list.Add(new cgEnPassantMove(moveSet.from, (byte)moveSet.moves[i],
                                             cgValueModifiers.AlphaBeta_Weight_Capture, _enPassantCapturesOn));
                continue;
            }

            if ((piece > 0 && squares[moveSet.moves[i]] > 0) || (piece <= 0 && squares[moveSet.moves[i]] < 0))
                rayOpen = false;                    // own piece blocks
        }
        // NOTE: the pawn capture section is not gated by rayOpen (only the push section is).
        // NOTE: an empty "from" square (piece == 0) is treated as black.
        return list;
    }

    // @ 0x1599e4e
    // NOTE: doAdvancedPinCheck is ignored; always calls findLegalMoves(asWhite, false, false).
    public List<cgSimpleMove> findStrictLegalMoves(bool asWhite, bool doAdvancedPinCheck)
    {
        List<cgSimpleMove> list = findLegalMoves(asWhite, false, false);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!verifyLegality(list[i]))
                list.RemoveAt(i);
        }
        return list;
    }

    // @ 0x15a0a59
    // Only KING moves are actually verified (play it, generate replies, reject if any reply lands on
    // the king's new square). The null move is reported illegal.
    public bool verifyLegality(cgSimpleMove testMove)
    {
        if (testMove.to == 65)
            return false;
        if (Math.Abs(squares[testMove.from]) == 6)
        {
            move(testMove);
            List<cgSimpleMove> replies = findLegalMoves(_whiteTurnToMove, true, false);
            foreach (cgSimpleMove reply in replies)
            {
                if (reply.to == testMove.to)
                {
                    revert();
                    return false;
                }
            }
            revert();
        }
        return true;
    }
    // NOTE: findLegalMoves overwrites _enPassantSquare/_enPassantCapturesOn for the hypothetical
    // position and revert() does not restore them (stale until the next findLegalMoves).
    // (Replies are generated with the king already on its new square, so attacks on it - including
    //  pawn diagonals and attacks on a captured, previously defended piece - show up as captures.)

    // @ 0x15a15d3
    public List<byte> _getBlockAttackSquares(cgSimpleMove forMove)
    {
        List<byte> list = new List<byte>();
        list.Add(forMove.from);                     // capturing the checker
        int t = Math.Abs(squares[forMove.from]);
        if (t == 2 || t == 4 || t == 5)             // rook / bishop / queen  (bitmask 0x34)
            list.AddRange(_getRayIn(_findMoveSetFor(t, forMove.from), forMove.to));
        return list;
    }

    // @ 0x15a17bf
    // Squares of the ray (in mset) that leads to `to`, excluding `to` itself.
    public List<byte> _getRayIn(cgMoveSet mset, byte to)
    {
        byte rayStart = 0;
        byte rayEnd = 0;
        for (byte i = 0; i < mset.moves.Count; i++)
        {
            if (mset.moves[i] == -1)
                rayStart = i;                       // NOTE: index OF the -1, not i+1
            else if (mset.moves[i] == to)
            {
                rayEnd = i;
                break;
            }
        }
        List<byte> list = new List<byte>();
        for (byte j = rayStart; j < rayEnd; j++)
            list.Add((byte)mset.moves[j]);
        // NOTE: for every ray but the first, the list starts with 255 (the -1 separator cast to byte).
        // Harmless for its only caller (no move has to == 255).
        return list;
    }

    // @ 0x15a132d
    // Whole ray (non-negative entries) of a slider's move set that contains includesIndex; null if the
    // set is not a slider or no ray contains it.
    public List<byte> _getFullRayFor(cgMoveSet set, byte includesIndex)
    {
        if (!(set.type == 2 || set.type == 4 || set.type == 5))   // (byte)type <= 5 && (0x34 >> type & 1)
            return null;

        byte rayStart = 0;
        byte rayEnd = 65;
        bool found = false;
        for (byte i = 0; i < set.moves.Count; i++)
        {
            if (!found && set.moves[i] == -1)
                rayStart = (byte)(i + 1);
            else if (found && set.moves[i] == -1)
            {
                rayEnd = i;
                break;
            }
            if (set.moves[i] == includesIndex)
                found = true;
        }
        if (!found)
            return null;

        List<byte> list = new List<byte>();
        for (byte j = rayStart; j < rayEnd; j++)
        {
            if (set.moves[j] >= 0)
                list.Add((byte)set.moves[j]);
        }
        // NOTE: if the matching ray were the last one with no trailing -1, rayEnd stays 65 and
        // set.moves[j] would throw ArgumentOutOfRange. cgMoveGenerator.FindMoves appears to append -1
        // after every direction, so this should not trigger in practice.
        return list;
    }
}

// @ 0x15a472f   cgBoard.<>c.<findLegalMoves>b__37_0
//   int (cgSimpleMove x, cgSimpleMove y) => x.positionalVal.CompareTo(y.positionalVal);
//   (sbyte.CompareTo(sbyte) == x - y). Cached in cgBoard.<>c.<>9__37_0.
