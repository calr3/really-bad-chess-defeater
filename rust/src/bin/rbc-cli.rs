//! Command-line tool.
//!
//!   rbc-cli plan <rank,rank,...> <games per rank> <firstSeed> <secondsPerMove> [threads]
//!       Planner (White) against the engine model at 3/5/5 (Black) on ranked boards, like the C#
//!       `harness plan`. Seeds: firstSeed + game * 7919 + rank.
//!   rbc-cli board <seed> <rank>          print the ranked starting position as a state string
//!   rbc-cli engine <state...>            the engine's move (3/5/5) in a state
//!   rbc-cli bench <pos.txt> [count]      single-threaded engine time on test-vector positions
//!   rbc-cli legal <state...>             the legal moves in a state
//!   rbc-cli choose <secs> <state...>     the planner's move in a state
use rbc::board::FastBoard;
use rbc::gen::ranked_board;
use rbc::planner::{now_secs, Planner};
use rbc::moves_to_string;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let state = |from: usize| FastBoard::from_state(&args[from..].join(" ")).unwrap_or_else(|e| die(&e));
    match args.first().map(|s| s.as_str()) {
        Some("plan") if args.len() >= 5 => plan(&args),
        Some("board") if args.len() == 3 => {
            println!("{}", ranked_board(num(&args[1]), num(&args[2])).to_state());
        }
        Some("engine") if args.len() >= 2 => match state(1).engine_move(3, 5, 5) {
            Some(m) => println!("{}-{} {m}", m.from, m.to),
            None => println!("none"),
        },
        Some("bench") if args.len() >= 2 => bench(&args[1], args.get(2).map(|n| num(n) as usize).unwrap_or(300)),
        Some("legal") if args.len() >= 2 => println!("{}", moves_to_string(&state(1).legal_moves())),
        Some("choose") if args.len() >= 3 => {
            let mut p = Planner::new();
            p.seconds_per_move = args[1].parse().unwrap_or_else(|_| die("bad seconds"));
            match p.choose(&state(2)) {
                Some(c) => println!("{}-{} {}{}", c.mv.from, c.mv.to, c.mv, c.win_in.map(|n| format!(" (win in {n})")).unwrap_or_default()),
                None => println!("none"),
            }
            eprintln!("{}", p.last_info);
        }
        _ => {
            eprintln!(
                "usage:\n  rbc-cli plan <ranks,...> <games> <firstSeed> <secsPerMove> [threads]\n  rbc-cli board <seed> <rank>\n  \
                 rbc-cli engine <state>\n  rbc-cli legal <state>\n  rbc-cli bench <pos.txt>\n  rbc-cli choose <secs> <state>"
            );
            std::process::exit(2);
        }
    }
}

fn die(msg: &str) -> ! {
    eprintln!("{msg}");
    std::process::exit(2)
}

fn num(s: &str) -> i32 {
    s.parse().unwrap_or_else(|_| die(&format!("not a number: {s}")))
}

fn plan(args: &[String]) {
    let ranks: Vec<i32> = args[1].split(',').map(num).collect();
    let games = num(&args[2]);
    let seed0 = num(&args[3]);
    let secs: f64 = args[4].parse().unwrap_or_else(|_| die("bad seconds"));
    let threads = args.get(5).map(|t| num(t) as usize);
    let (mut wins, mut total, mut total_moves, mut max_moves) = (0, 0, 0, 0);
    let t_all = now_secs();
    for &rank in &ranks {
        for g in 0..games {
            let seed = seed0 + g * 7919 + rank;
            let mut b = ranked_board(seed, rank);
            let start = b.to_string();
            let mut planner = Planner::new();
            planner.seconds_per_move = secs;
            if let Some(t) = threads {
                planner.threads = t;
            }
            let t0 = now_secs();
            let (mut log, mut result, mut moves) = (String::new(), "DRAW", 0);
            for _ in 0..200 {
                let legal = b.legal_moves();
                if legal.is_empty() {
                    result = "LOSS";
                    break;
                }
                let c = planner.choose(&b).unwrap();
                let m = *legal.iter().find(|x| x.from == c.mv.from && x.to == c.mv.to).unwrap();
                moves += 1;
                log += &format!(" {moves}.{m}{}", c.win_in.map(|n| format!("(#{n})")).unwrap_or_default());
                b.make(&m);
                if !b.sq.contains(&-6) {
                    result = "WIN";
                    break;
                }
                let blegal = b.legal_moves();
                let Some(r) = b.engine_move(3, 5, 5).filter(|_| !blegal.is_empty()) else {
                    result = "WIN"; // the opponent has no move: mated or stalemated, and loses
                    break;
                };
                let r = *blegal.iter().find(|x| x.from == r.from && x.to == r.to).expect("engine move not legal");
                log += &format!(" {r}");
                b.make(&r);
                planner.observe(m, r);
                if !b.sq.contains(&6) {
                    result = "LOSS";
                    break;
                }
                eprintln!("  rank {rank} seed {seed} move {moves}: {}", planner.last_info);
            }
            total += 1;
            if result == "WIN" {
                wins += 1;
                total_moves += moves;
                max_moves = max_moves.max(moves);
            }
            println!("rank {rank} seed {seed} start {start}: {result} in {moves} moves, {:.0}s", now_secs() - t0);
            println!("   {log}");
        }
    }
    println!(
        "TOTAL {wins}/{total} wins, avg {:.2} moves, max {max_moves}, {:.0} min",
        total_moves as f64 / wins.max(1) as f64,
        (now_secs() - t_all) / 60.0
    );
}

fn bench(path: &str, count: usize) {
    let text = std::fs::read_to_string(path).unwrap_or_else(|e| die(&format!("{path}: {e}")));
    let lines: Vec<&str> = text.lines().take(count).collect();
    let t0 = now_secs();
    for l in &lines {
        let f: Vec<&str> = l.split(" | ").collect();
        let d: Vec<u8> = f[7].split(' ').take(3).map(|x| x.parse().unwrap()).collect();
        FastBoard::from_state(f[0]).unwrap().engine_root(d[0], d[1], d[2]);
    }
    println!("Rust engine_root: {} positions, {:.1} ms each", lines.len(), (now_secs() - t0) * 1000.0 / lines.len() as f64);
}
