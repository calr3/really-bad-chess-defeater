using System;
using System.Diagnostics;

static class PlanGames
{
    // usage: harness plan <rank,rank,...> <games per rank> <firstSeed> <secondsPerMove> [threads]
    public static void Run(string[] args)
    {
        var ranks = args[1].Split(',');
        int games = int.Parse(args[2]), seed0 = int.Parse(args[3]);
        double secs = double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
        int threads = args.Length > 5 ? int.Parse(args[5]) : Environment.ProcessorCount;
        new cgBoard(null);
        int wins = 0, total = 0, totalMoves = 0, maxMoves = 0, mispredict = 0;
        var all = Stopwatch.StartNew();
        foreach (var rs in ranks)
            for (int g = 0; g < games; g++)
            {
                int rank = int.Parse(rs), seed = seed0 + g * 7919 + rank;
                var b = cgChessBoardScript.RankedBoard(seed, rank);
                var start = FastBoard.From(b, false).ToString();
                var planner = new Planner { SecondsPerMove = secs, Threads = threads };
                var rbc = new cgEngine { SearchDepthWeak = 3, SearchDepthStrong = 5, SearchDepthEndGame = 5 };
                var sw = Stopwatch.StartNew();
                string log = "", result = "DRAW";
                int moves = 0;
                for (int ply = 0; ply < 200; ply++)
                {
                    bool white = b.whiteTurnToMove;
                    var legal = Exploiter.Legal(b);
                    if (legal.Count == 0) { result = white ? "LOSS" : "WIN"; break; }
                    if (white)
                    {
                        var fm = planner.Choose(FastBoard.From(b, true), out bool won, out int winIn);
                        var m = legal.Find(x => x.from == fm.From && x.to == fm.To);
                        moves++;
                        log += $" {moves}.{cgGlobal.MoveToString(m)}" + (won ? $"(#{winIn})" : "");
                        b.move(m);
                        if (!HasKing(b, false)) { result = "WIN"; break; }
                        // what does the model predict?
                        var after = FastBoard.From(b, false);
                        bool predicted = planner.Reply(after, out var pred);
                        var rm = rbc.FindMove(b, false);
                        if (rm == null) { result = "WIN"; break; }
                        if (!predicted || pred.From != rm.from || pred.To != rm.to) { mispredict++; log += " [MISPREDICT]"; }
                        rm = Exploiter.Legal(b).Find(x => x.from == rm.from && x.to == rm.to);
                        log += " " + cgGlobal.MoveToString(rm);
                        b.move(rm);
                        planner.Observe(fm, new FMove(rm.from, rm.to, 0));
                        if (!HasKing(b, true)) { result = "LOSS"; break; }
                        Console.Error.WriteLine($"  rank {rank} seed {seed} move {moves}: {planner.LastInfo}");
                    }
                }
                total++; if (result == "WIN") { wins++; totalMoves += moves; maxMoves = Math.Max(maxMoves, moves); }
                Console.WriteLine($"rank {rank} seed {seed} start {start}: {result} in {moves} moves, {sw.Elapsed.TotalSeconds:F0}s");
                Console.WriteLine("   " + log);
            }
        Console.WriteLine($"TOTAL {wins}/{total} wins, avg {(double)totalMoves / Math.Max(1, wins):F2} moves, max {maxMoves}, mispredictions {mispredict}, {all.Elapsed.TotalMinutes:F0} min");
    }

    static bool HasKing(cgBoard b, bool white)
    {
        foreach (var s in b.squares) if (s == (white ? 6 : -6)) return true;
        return false;
    }
}
