//! Checks the port against vectors written by the C# harness (`harness vectors <dir> 1500 1`),
//! whose model was checked move-for-move against the faithful decompilation.

use rbc::board::FastBoard;
use rbc::gen::{random_board, Mode, UnityRandom};
use rbc::sort::dotnet_sort;
use rbc::FMove;

fn data(name: &str) -> String {
    let p = format!("{}/tests/data/{name}", env!("CARGO_MANIFEST_DIR"));
    std::fs::read_to_string(&p).unwrap_or_else(|e| panic!("{p}: {e}"))
}

#[test]
fn unity_random() {
    for line in data("rng.txt").lines() {
        let f: Vec<&str> = line.split(' ').collect();
        let mut r = UnityRandom::new(f[0].parse().unwrap());
        let floats = |s: &str| -> Vec<u32> { s.split(',').map(|x| u32::from_str_radix(x, 16).unwrap()).collect() };
        let ints = |s: &str| -> Vec<i32> { s.split(',').map(|x| x.parse().unwrap()).collect() };
        for b in floats(f[2]) {
            assert_eq!(r.value().to_bits(), b, "value, seed {}", f[0]);
        }
        for b in floats(f[4]) {
            assert_eq!(r.range_f(3.0, 5.5).to_bits(), b, "range_f, seed {}", f[0]);
        }
        for v in ints(f[6]) {
            assert_eq!(r.range_i(1, 6), v);
        }
        for v in ints(f[8]) {
            assert_eq!(r.range_i(7, -3), v);
        }
    }
}

#[test]
fn generator() {
    let mut n = 0;
    for line in data("gen.txt").lines() {
        let f: Vec<&str> = line.split(' ').collect();
        let seed: i32 = f[1].parse().unwrap();
        let param: i32 = f[2].parse().unwrap();
        let mode = match f[0] {
            "ranked" => Mode::Ranked(param),
            "freeplay" => Mode::Freeplay(param),
            "daily" => Mode::Daily,
            "weekly" => Mode::Weekly,
            m => panic!("mode {m}"),
        };
        let b = random_board(seed, mode).board;
        assert_eq!(b.to_string(), f[3], "{line}");
        n += 1;
    }
    assert!(n > 800);
}

#[test]
fn dotnet_sort_matches() {
    // sort-extra.txt: arrays found to reach the heapsort fallback (rare in practice)
    for (ln, line) in data("sort.txt").lines().chain(data("sort-extra.txt").lines()).enumerate() {
        let (l, r) = line.split_once(" | ").unwrap_or((line.trim_end_matches(" |"), ""));
        let mut it = l.split(' ');
        let n: usize = it.next().unwrap().parse().unwrap();
        let keys: Vec<i32> = it.filter(|x| !x.is_empty()).map(|x| x.parse().unwrap()).collect();
        assert_eq!(keys.len(), n);
        let want: Vec<usize> = r.split(' ').filter(|x| !x.is_empty()).map(|x| x.parse().unwrap()).collect();
        let mut v: Vec<(i32, usize)> = keys.iter().copied().zip(0..).collect();
        dotnet_sort(&mut v, &|a: &(i32, usize), b: &(i32, usize)| a.0.cmp(&b.0) as i32);
        let got: Vec<usize> = v.iter().map(|x| x.1).collect();
        assert_eq!(got, want, "line {}", ln + 1);
    }
}

fn mv_str(m: &FMove) -> String {
    let mut s = format!("{}-{}", m.from, m.to);
    if m.kind != 0 {
        s += &format!("k{}", m.kind);
    }
    s
}
fn list(ms: &[FMove]) -> String {
    if ms.is_empty() {
        "-".into()
    } else {
        ms.iter().map(mv_str).collect::<Vec<_>>().join(" ")
    }
}

struct Pos {
    line: String,
    fields: Vec<String>,
}

fn positions() -> Vec<Pos> {
    data("pos.txt")
        .lines()
        .map(|l| Pos { line: l.to_string(), fields: l.split(" | ").map(|s| s.to_string()).collect() })
        .collect()
}

#[test]
fn state_roundtrip_key_eval_check() {
    for p in positions() {
        let mut b = FastBoard::from_state(&p.fields[0]).unwrap();
        assert_eq!(b.to_state(), p.fields[0]);
        let want: Vec<&str> = p.fields[1].split(' ').collect();
        assert_eq!(b.key().to_string(), want[0], "key: {}", p.line);
        assert_eq!(b.evaluate().to_string(), want[1], "eval: {}", p.line);
        assert_eq!((b.in_check(true) as u8).to_string(), want[2], "check W: {}", p.line);
        assert_eq!((b.in_check(false) as u8).to_string(), want[3], "check B: {}", p.line);
    }
}

#[test]
fn move_generation() {
    for p in positions() {
        let mut b = FastBoard::from_state(&p.fields[0]).unwrap();
        assert_eq!(list(&b.legal_moves()), p.fields[2], "strict legal: {}", p.fields[0]);
        assert_eq!(list(&b.pseudo_legal(true, false)), p.fields[3], "pseudo W: {}", p.fields[0]);
        assert_eq!(list(&b.pseudo_legal(false, false)), p.fields[4], "pseudo B: {}", p.fields[0]);
        assert_eq!(list(&b.pseudo_legal(true, true)), p.fields[5], "pseudo W disregard: {}", p.fields[0]);
        assert_eq!(list(&b.pseudo_legal(false, true)), p.fields[6], "pseudo B disregard: {}", p.fields[0]);
    }
}

#[test]
fn make_revert_restores() {
    for p in positions() {
        let mut b = FastBoard::from_state(&p.fields[0]).unwrap();
        let before = (b.to_state(), b.evaluate());
        for m in b.legal_moves() {
            b.make(&m);
            let mut ep = m.kind == 2;
            for r in b.legal_moves() {
                ep |= r.kind == 2;
                b.make(&r);
                b.revert();
            }
            b.revert();
            // NOTE: the game's en passant does not count the capture, but its undo does, so the
            // piece count goes up by one. Everything else must be restored.
            let after = b.to_state();
            if ep {
                b.remaining = FastBoard::from_state(&before.0).unwrap().remaining;
            }
            assert_eq!((b.to_state(), b.evaluate()), before, "after {} (was {after})", mv_str(&m));
        }
    }
}

/// The engine's root list, with every value and the order the game's sorts leave it in.
#[test]
fn engine_matches() {
    let ps = positions();
    let threads = std::thread::available_parallelism().map(|n| n.get()).unwrap_or(1);
    let chunks: Vec<&[Pos]> = ps.chunks(ps.len().div_ceil(threads)).collect();
    let bad: usize = std::thread::scope(|s| {
        let hs: Vec<_> = chunks
            .iter()
            .map(|c| {
                s.spawn(move || {
                    let mut bad = 0;
                    for p in c.iter() {
                        let b = FastBoard::from_state(&p.fields[0]).unwrap();
                        let f: Vec<&str> = p.fields[7].split(' ').collect();
                        let (w, st, e) = (f[0].parse().unwrap(), f[1].parse().unwrap(), f[2].parse().unwrap());
                        let got: Vec<String> =
                            b.engine_root(w, st, e).iter().map(|m| format!("{}:{}", mv_str(m), m.val)).collect();
                        if got.join(" ") != f[3..].join(" ") {
                            bad += 1;
                            if bad <= 3 {
                                eprintln!("MISMATCH {}\n  want {}\n  got  {}", p.fields[0], f[3..].join(" "), got.join(" "));
                            }
                        }
                    }
                    bad
                })
            })
            .collect();
        hs.into_iter().map(|h| h.join().unwrap()).sum()
    });
    assert_eq!(bad, 0, "{bad} of {} engine root lists differ", ps.len());
}
