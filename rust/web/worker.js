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
};

onmessage = async ({ data: { id, op, args } }) => {
  try {
    await ready;
    postMessage({ id, ok: true, value: ops[op](args || {}) });
  } catch (e) {
    postMessage({ id, ok: false, error: String(e?.message ?? e) });
  }
};
