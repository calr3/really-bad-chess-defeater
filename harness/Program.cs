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

    static void Main(string[] args)
    {
        var ex = new Exploiter();
        new cgBoard(null);
        foreach (bool exWhite in new[] { true, false })
        {
            var b = new cgBoard(null);
            var sw = Stopwatch.StartNew();
            int r = PlayGame(b, exWhite, ex, 300, out int plies, out string log);
            Console.WriteLine($"exploiter {(exWhite ? "white" : "black")}: result {r} in {plies} plies, {sw.Elapsed.TotalSeconds:F0}s, model calls {ex.ModelCalls}");
            Console.WriteLine(log);
        }
    }
}
