// Writes reference test vectors for ports of the model (see rust/). Every value comes from the C#
// code that was checked against the faithful decompilation, so a port that reproduces these files
// reproduces the game's engine.
//
// usage: harness vectors <outdir> <positions> [seed]
//
// Files (one record per line, space separated):
//   rng.txt   <seed> F <8 x value, as float bits in hex> G <8 x Range(3f,5.5f), bits> I <8 x Range(1,6)>
//             J <8 x Range(7,-3)>, all drawn in that order after InitState(seed)
//   gen.txt   <mode> <seed> <rank or difficulty> <board>   generator output (ranked/freeplay/daily/weekly)
//   sort.txt  <n> <keys...> | <order...>                    Span.Sort(Comparison) on (key, tag) pairs; order = tags
//   sort-extra.txt                                          the same for the arrays in sort-inputs.txt, if present
//   pos.txt   <state> | <key> <eval> <inCheckW> <inCheckB> | <strict legal> | <pseudo W> | <pseudo B>
//             | <pseudo W disregard> | <pseudo B disregard> | <weak strong end> <engine root: f-t:val ...>
//
// <state> = <64-char board> <w|b> <wc> <bc> <wa> <wh> <ba> <bh> <wk> <bk> <remaining> <lastFrom> <lastTo>
// (lastFrom/lastTo = 255 when there is no previous move). Moves are written as from-to[k], where
// k is the kind (0 normal, 1 castle, 2 en passant, 3 null) when nonzero.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

static class Vectors
{
    public static void Run(string[] args)
    {
        string dir = args[1];
        int positions = int.Parse(args[2]);
        int seed0 = args.Length > 3 ? int.Parse(args[3]) : 1;
        Directory.CreateDirectory(dir);
        new cgBoard(null);

        // ---- rng
        using (var w = new StreamWriter(Path.Combine(dir, "rng.txt")))
            foreach (int s in new[] { 0, 1, -1, 5000, 12919, 123456789, int.MinValue, int.MaxValue })
            {
                UnityRandomReference.InitState(s);
                var f = Enumerable.Range(0, 8).Select(_ => BitConverter.SingleToInt32Bits(UnityRandomReference.value).ToString("x8"));
                var g = Enumerable.Range(0, 8).Select(_ => BitConverter.SingleToInt32Bits(UnityRandomReference.Range(3f, 5.5f)).ToString("x8"));
                var i = Enumerable.Range(0, 8).Select(_ => UnityRandomReference.Range(1, 6).ToString());
                var j = Enumerable.Range(0, 8).Select(_ => UnityRandomReference.Range(7, -3).ToString());
                w.WriteLine($"{s} F {string.Join(",", f)} G {string.Join(",", g)} I {string.Join(",", i)} J {string.Join(",", j)}");
            }

        // ---- generator
        using (var w = new StreamWriter(Path.Combine(dir, "gen.txt")))
        {
            for (int rank = -5; rank <= 120; rank += 1)
                for (int k = 0; k < 4; k++)
                {
                    int seed = seed0 * 1000003 + rank * 7919 + k * 104729;
                    var b = cgChessBoardScript.RankedBoard(seed, rank);
                    w.WriteLine($"ranked {seed} {rank} {FastBoard.From(b, false)}");
                }
            for (int k = 0; k < 120; k++)
            {
                int seed = seed0 * 7777 + k * 15485863;
                var s = new cgChessBoardScript();
                s.modeName.text = "freeplay"; s.freeplayDifficultySlider.curDifficulty = k % 101;
                w.WriteLine($"freeplay {seed} {k % 101} {FastBoard.From(s.getRandomBoard(seed, -1, 0), false)}");
                s.modeName.text = "daily";
                int day = 20260101 + k % 28 + (k / 28) * 100;
                w.WriteLine($"daily {day} 0 {FastBoard.From(s.getRandomBoard(day, -1, 0), false)}");
                s.modeName.text = "weekly";
                w.WriteLine($"weekly {30260000 + k} 0 {FastBoard.From(s.getRandomBoard(30260000 + k, -1, 0), false)}");
            }
        }

        // ---- sort
        var rnd = new Random(seed0);
        using (var w = new StreamWriter(Path.Combine(dir, "sort.txt")))
            for (int t = 0; t < 3000; t++)
            {
                int n = t < 40 ? t : rnd.Next(2, t % 3 == 0 ? 400 : 120);
                int range = rnd.Next(1, 4) switch { 1 => 3, 2 => 20, _ => 1000 };
                var keys = new int[n];
                int pattern = rnd.Next(6);
                for (int i = 0; i < n; i++)
                    keys[i] = pattern switch
                    {
                        0 => i, 1 => n - i, 2 => 7, 3 => (i % 2 == 0 ? i : n - i),
                        _ => rnd.Next(-range, range)
                    };
                var arr = keys.Select((k, i) => (k, i)).ToArray();
                arr.AsSpan().Sort((x, y) => x.k.CompareTo(y.k));
                w.WriteLine($"{n} {string.Join(" ", keys)} | {string.Join(" ", arr.Select(x => x.i))}");
            }

        // ---- extra sort inputs (sort-inputs.txt, one array per line), e.g. ones that reach heapsort
        var extra = Path.Combine(dir, "sort-inputs.txt");
        if (File.Exists(extra))
            File.WriteAllLines(Path.Combine(dir, "sort-extra.txt"), File.ReadAllLines(extra).Where(l => l.Trim() != "").Select(l =>
            {
                var keys = l.Trim().Split(' ').Select(int.Parse).ToArray();
                var arr = keys.Select((k, i) => (k, i)).ToArray();
                arr.AsSpan().Sort((x, y) => x.k.CompareTo(y.k));
                return $"{keys.Length} {string.Join(" ", keys)} | {string.Join(" ", arr.Select(x => x.i))}";
            }));

        // ---- positions: random playouts from generated boards, partly following the engine
        var specs = Enumerable.Range(0, positions).Select(p =>
        {
            var r = new Random(seed0 * 7 + p);
            return (p, rank: r.Next(0, 101), plies: r.Next(0, 40), r);
        }).ToList();
        var lines = new string[positions];
        int done = 0;
        Parallel.ForEach(specs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, sp =>
        {
            var r = sp.r;
            var fb = FastBoard.From(cgChessBoardScript_Locked(seed0 * 31 + sp.p, sp.rank), false);
            bool cheap = sp.p % 5 == 4;
            byte weak = cheap ? (byte)2 : (byte)3, strong = cheap ? (byte)3 : (byte)5, end = cheap ? (byte)3 : (byte)5;
            for (int k = 0; k < sp.plies; k++)
            {
                var ms = fb.LegalMoves();
                if (ms.Length == 0) break;
                FMove m;
                if (r.NextDouble() < 0.25 && FastBoard.EngineMove(fb, 1, 2, 2, out var em)) m = em;
                else
                {
                    // prefer captures, castling and double pawn steps now and then, to reach the quirky paths
                    var special = ms.Where(x => x.Kind != 0 || fb.Sq[x.To] != 0 || Math.Abs(x.From - x.To) == 16).ToArray();
                    m = special.Length > 0 && r.NextDouble() < 0.4 ? special[r.Next(special.Length)] : ms[r.Next(ms.Length)];
                }
                fb.Move(m);
                if (!fb.Sq.Contains((sbyte)6) || !fb.Sq.Contains((sbyte)-6)) break;
            }
            var root = FastBoard.EngineRoot(fb, weak, strong, end);
            lines[sp.p] = string.Join(" | ",
                State(fb),
                $"{fb.Key()} {fb.Evaluate()} {(fb.InCheck(true) ? 1 : 0)} {(fb.InCheck(false) ? 1 : 0)}",
                Moves(fb.LegalMoves()),
                Moves(fb.FindLegalCopy(true, false)), Moves(fb.FindLegalCopy(false, false)),
                Moves(fb.FindLegalCopy(true, true)), Moves(fb.FindLegalCopy(false, true)),
                $"{weak} {strong} {end} " + string.Join(" ", root.Select(m => M(m) + ":" + m.Val)));
            int d = System.Threading.Interlocked.Increment(ref done);
            if (d % 100 == 0) Console.Error.WriteLine($"  {d}/{positions}");
        });
        File.WriteAllLines(Path.Combine(dir, "pos.txt"), lines);
        Console.WriteLine($"wrote vectors to {dir}");
    }

    /// <summary>harness bench <pos.txt> [count]: single-threaded EngineRoot time on vector positions.</summary>
    public static void Bench(string[] args)
    {
        new cgBoard(null);
        var lines = File.ReadAllLines(args[1]).Take(args.Length > 2 ? int.Parse(args[2]) : 300).ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var l in lines)
        {
            var f = l.Split(" | ");
            var d = f[7].Split(' ');
            FastBoard.EngineRoot(ParseState(f[0]), byte.Parse(d[0]), byte.Parse(d[1]), byte.Parse(d[2]));
        }
        Console.WriteLine($"C# EngineRoot: {lines.Length} positions, {sw.Elapsed.TotalMilliseconds / lines.Length:F1} ms each");
    }

    static FastBoard ParseState(string s)
    {
        var f = s.Split(' ');
        var b = new cgBoard(null);
        for (int i = 0; i < 64; i++) b.squares[i] = (sbyte)("kqbnrp.PRNBQK".IndexOf(f[0][i]) - 6);
        b._whiteTurnToMove = f[1] == "w";
        b.whiteHasCastled = f[2] == "1"; b.blackHasCastled = f[3] == "1";
        b._whiteARookMoves = byte.Parse(f[4]); b._whiteHRookMoves = byte.Parse(f[5]);
        b._blackARookMoves = byte.Parse(f[6]); b._blackHRookMoves = byte.Parse(f[7]);
        b._whiteKingMoves = byte.Parse(f[8]); b._blackKingMoves = byte.Parse(f[9]);
        b.remainingPieces = byte.Parse(f[10]);
        if (f[11] != "255") b.moves.Add(new cgSimpleMove(byte.Parse(f[11]), byte.Parse(f[12]), 0));
        return FastBoard.From(b, true);
    }

    static readonly object _genLock = new();
    // the generator uses the global UnityEngine.Random state and SaveData, so serialise it
    static cgBoard cgChessBoardScript_Locked(int seed, int rank) { lock (_genLock) return cgChessBoardScript.RankedBoard(seed, rank); }

    public static string State(FastBoard f)
    {
        byte lf = 255, lt = 255;
        if (f.HasLast) { lf = f.Last.From; lt = f.Last.To; }
        return $"{f} {(f.WhiteTurn ? "w" : "b")} {(f.WCastled ? 1 : 0)} {(f.BCastled ? 1 : 0)} {f.WA} {f.WH} {f.BA} {f.BH} {f.WK} {f.BK} {f.Remaining} {lf} {lt}".Replace("/", "");
    }

    static string M(FMove m) => $"{m.From}-{m.To}" + (m.Kind != 0 ? $"k{m.Kind}" : "");
    static string Moves(FMove[] ms) => ms.Length == 0 ? "-" : string.Join(" ", ms.Select(M));
}
