// Runs the game's engine model and the planner off the page's thread (they block while thinking).
// The page sends {id, op, args} and gets back {id, ok, value} or {id, ok: false, error}.
import init, * as rbc from './pkg/rbc.js';

const ready = init();
let planner = null;

const ops = {
  ranked: ({ seed, rank }) => rbc.rankedBoard(seed, rank),
  legal: ({ state }) => rbc.legalMoves(state),
  play: ({ state, f, t }) => rbc.play(state, f, t),
  engine: ({ state }) => rbc.engineMove(state, 3, 5, 5),
  planner: ({ seconds }) => {
    planner?.free();
    planner = new rbc.Planner(seconds);
  },
  choose: ({ state }) => {
    const t0 = performance.now();
    const c = planner.choose(state);
    return { c, secs: (performance.now() - t0) / 1000, info: planner.lastInfo() };
  },
  observe: ({ of, ot, rf, rt }) => planner.observe(of, ot, rf, rt),
  planLine: () => (planner ? planner.planLine() : ''),
  setPlanLine: ({ line }) => planner.setPlanLine(line),
  // Standard notation for a game: the starting position as FEN and each move in SAN.
  notation: ({ start, moves }) => {
    const squares = s => s.split(' ')[0];
    const legal = s => { const l = rbc.legalMoves(s); return l ? l.split(' ').map(x => x.split('-').map(Number)) : []; };
    const name = i => 'abcdefgh'[i & 7] + (8 - (i >> 3));
    // can the side to move capture the other side's king?
    const hitsKing = s => {
      const b = squares(s), k = b.indexOf(s.split(' ')[1] === 'w' ? 'k' : 'K');
      return k >= 0 && legal(s).some(([, t]) => t === k);
    };
    const flip = s => { const p = s.split(' '); p[1] = p[1] === 'w' ? 'b' : 'w'; p[11] = p[12] = '255'; return p.join(' '); };
    const fen = s => {
      const p = s.split(' '), b = p[0], rows = [];
      for (let r = 0; r < 8; r++) {
        let row = '', empty = 0;
        for (let c = 0; c < 8; c++) {
          const ch = b[r * 8 + c];
          if (ch === '.') empty++; else { if (empty) row += empty; empty = 0; row += ch; }
        }
        rows.push(row + (empty || ''));
      }
      const [side, wc, bc, wa, wh, ba, bh, wk, bk] = p.slice(1, 10);
      let castle = '';
      if (wc === '0' && wk === '0' && b[60] === 'K') { if (wh === '0' && b[63] === 'R') castle += 'K'; if (wa === '0' && b[56] === 'R') castle += 'Q'; }
      if (bc === '0' && bk === '0' && b[4] === 'k') { if (bh === '0' && b[7] === 'r') castle += 'k'; if (ba === '0' && b[0] === 'r') castle += 'q'; }
      let ep = '-';
      const lf = +p[11], lt = +p[12];
      if (lf !== 255 && lt !== 255 && b[lt].toLowerCase() === 'p' && Math.abs(lf - lt) === 16) ep = name((lf + lt) / 2);
      return `${rows.join('/')} ${side} ${castle || '-'} ${ep} 0 1`;
    };
    const san = [];
    let s = start;
    for (const { f, t } of moves) {
      const b = squares(s), pc = b[f], P = pc.toUpperCase();
      const capture = b[t] !== '.' || (P === 'P' && (f & 7) !== (t & 7));
      const next = rbc.play(s, f, t);
      let out;
      if (P === 'K' && Math.abs((f & 7) - (t & 7)) === 2) out = (t & 7) > (f & 7) ? 'O-O' : 'O-O-O';
      else if (P === 'P') {
        out = (capture ? 'abcdefgh'[f & 7] + 'x' : '') + name(t);
        const now = squares(next)[t];
        if (now.toUpperCase() !== 'P') out += '=' + now.toUpperCase();
      } else {
        const others = legal(s).filter(([f2, t2]) => t2 === t && f2 !== f && b[f2] === pc).map(([f2]) => f2);
        let dis = '';
        if (others.length) {
          if (others.every(o => (o & 7) !== (f & 7))) dis = 'abcdefgh'[f & 7];
          else if (others.every(o => (o >> 3) !== (f >> 3))) dis = String(8 - (f >> 3));
          else dis = name(f);
        }
        out = P + dis + (capture ? 'x' : '') + name(t);
      }
      const enemyKing = P === pc ? 'k' : 'K';
      if (squares(next).includes(enemyKing) && hitsKing(flip(next))) {
        // mate: every reply still leaves the king capturable
        const mate = legal(next).every(([f2, t2]) => hitsKing(rbc.play(next, f2, t2)));
        out += mate ? '#' : '+';
      }
      san.push(out);
      s = next;
    }
    return { fen: fen(start), san };
  },
  // The moves (none, one or two) that lead from `state` to a position whose squares are `target`.
  match: ({ state, target }) => {
    const squares = s => s.split(' ')[0];
    const moves = s => { const l = rbc.legalMoves(s); return l ? l.split(' ').map(x => x.split('-').map(Number)) : []; };
    if (squares(state) === target) return [];
    const first = moves(state).map(([f, t]) => ({ f, t, s: rbc.play(state, f, t) }));
    for (const m of first) if (squares(m.s) === target) return [{ f: m.f, t: m.t }];
    for (const m of first) {
      if (!squares(m.s).includes('k') || !squares(m.s).includes('K')) continue;
      for (const [f, t] of moves(m.s)) if (squares(rbc.play(m.s, f, t)) === target) return [{ f: m.f, t: m.t }, { f, t }];
    }
    return null;
  },
};

onmessage = async ({ data: { id, op, args } }) => {
  try {
    await ready;
    postMessage({ id, ok: true, value: ops[op](args || {}) });
  } catch (e) {
    postMessage({ id, ok: false, error: String(e?.message ?? e) });
  }
};
