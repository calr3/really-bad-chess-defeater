using System;
using System.Collections.Generic;
using System.Diagnostics;

static class Program
{
    // Plays one game. Returns +1 if the exploiter wins, -1 if it loses, 0 if the ply limit is hit.
    public static int PlayGame(cgBoard b, bool exploiterWhite, Exploiter ex, int maxPlies, out int plies, out string log)
    {
        var rbc = new cgEngine { SearchDepthWeak = ex.OppWeak, SearchDepthStrong = ex.OppStrong, SearchDepthEndGame = ex.OppEnd };
        log = "";
        for (plies = 0; plies < maxPlies; plies++)
        {
            bool white = b.whiteTurnToMove;
            bool exTurn = white == exploiterWhite;
            if (Exploiter.Legal(b).Count == 0) return exTurn ? -1 : 1;   // mated or stalemated: loses
            cgSimpleMove m = exTurn ? ex.Choose(b) : rbc.FindMove(b, white);
            if (m == null) return exTurn ? -1 : 1;
            if (!exTurn) m = FindIn(b, m);
            log += (plies % 2 == 0 ? $" {plies / 2 + 1}." : " ") + cgGlobal.MoveToString(m);
            b.move(m);
            bool kw = false, kb = false;
            foreach (var s in b.squares) { if (s == 6) kw = true; if (s == -6) kb = true; }
            if (!kw) return exploiterWhite ? -1 : 1;
            if (!kb) return exploiterWhite ? 1 : -1;
        }
        return 0;
    }

    static cgSimpleMove FindIn(cgBoard b, cgSimpleMove m)
    {
        foreach (var x in Exploiter.Legal(b)) if (x.from == m.from && x.to == m.to) return x;
        throw new Exception("engine move not legal on game board: " + cgGlobal.MoveToString(m));
    }

    static string Show(cgBoard b)
    {
        const string P = "kqbnrp.PRNBQK";
        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < 8; r++) { for (int f = 0; f < 8; f++) sb.Append(P[b.squares[r * 8 + f] + 6]); sb.Append(r < 7 ? "/" : ""); }
        return sb.ToString();
    }

    static void Main(string[] args)
    {
        // usage: harness <rank,rank,...> <games per rank> [firstSeed]   |   harness check <positions> [seed]
        if (args.Length > 0 && args[0] == "plan") { PlanGames.Run(args); return; }
        if (args.Length > 0 && args[0] == "check") { new cgBoard(null); ModelCheck.Run(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 1); return; }
        var ranks = (args.Length > 0 ? args[0] : "15").Split(',');
        int games = args.Length > 1 ? int.Parse(args[1]) : 1;
        int seed0 = args.Length > 2 ? int.Parse(args[2]) : 1000;
        new cgBoard(null);
        int wins = 0, total = 0; long totalPlies = 0;
        foreach (var rs in ranks)
            for (int g = 0; g < games; g++)
            {
                int rank = int.Parse(rs), seed = seed0 + g * 7919 + rank;
                var b = cgChessBoardScript.RankedBoard(seed, rank);
                string start = Show(b);
                var ex = new Exploiter { OppWeak = 3, OppStrong = 5, OppEnd = 5 };
                var sw = Stopwatch.StartNew();
                int r = PlayGame(b, true, ex, 300, out int plies, out string log);
                total++; if (r == 1) wins++; totalPlies += plies;
                Console.WriteLine($"rank {rank} seed {seed} spread {b.currentGameSpread} start {start}: {(r == 1 ? "WIN" : r == -1 ? "LOSS" : "DRAW")} in {(plies + 1) / 2} moves, {sw.Elapsed.TotalSeconds:F0}s, model calls {ex.ModelCalls}");
                if (r != 1 || args.Length > 3) Console.WriteLine("   " + log);
            }
        Console.WriteLine($"TOTAL {wins}/{total} wins, avg {(double)totalPlies / total / 2:F1} moves");
    }
}
