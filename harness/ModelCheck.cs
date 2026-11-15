using System;
using System.Diagnostics;

static class ModelCheck
{
    // Compares FastBoard.EngineMove with the faithful cgEngine on random reachable positions.
    public static void Run(int positions, int seed0)
    {
        var rnd = new Random(seed0);
        int same = 0, total = 0;
        double tSlow = 0, tFast = 0;
        for (int p = 0; p < positions; p++)
        {
            var b = cgChessBoardScript.RankedBoard(seed0 + p, rnd.Next(0, 101));
            int plies = rnd.Next(0, 30);
            for (int k = 0; k < plies; k++)
            {
                var ms = Exploiter.Legal(b);
                if (ms.Count == 0) break;
                b.move(ms[rnd.Next(ms.Count)]);
            }
            if (Exploiter.Legal(b).Count == 0) continue;
            var sw = Stopwatch.StartNew();
            var e = new cgEngine { SearchDepthWeak = 3, SearchDepthStrong = 5, SearchDepthEndGame = 5 };
            var slow = e.FindMove(b, b.whiteTurnToMove);
            tSlow += sw.Elapsed.TotalMilliseconds; sw.Restart();
            var fb = FastBoard.From(b, true);
            bool ok = FastBoard.EngineMove(fb, 3, 5, 5, out var fast);
            tFast += sw.Elapsed.TotalMilliseconds;
            total++;
            bool match = ok && slow != null && slow.from == fast.From && slow.to == fast.To;
            // also compare the strict legal move lists for the side to move (with history)
            var a = Exploiter.Legal(b); var f = fb.LegalMoves();
            bool lm = a.Count == f.Length;
            for (int i = 0; lm && i < a.Count; i++) lm = a[i].from == f[i].From && a[i].to == f[i].To;
            if (match && lm) same++;
            else Console.WriteLine($"MISMATCH pos {p}: {fb} white={b.whiteTurnToMove} slow={cgGlobal.MoveToString(slow)} fast={(ok ? fast.ToString() : "none")} legalListsEqual={lm}");
        }
        Console.WriteLine($"model check: {same}/{total} identical; slow {tSlow / total:F0} ms, fast {tFast / total:F1} ms per move");
    }
}
