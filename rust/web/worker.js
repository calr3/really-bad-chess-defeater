// Runs the planner and the engine model off the page's thread (they block while thinking).
import init, * as rbc from './pkg/rbc.js';

let ready = init();
let planner = null;

onmessage = async ({ data: msg }) => {
  await ready;
  if (msg.type === 'new') {
    planner = new rbc.Planner(msg.seconds);
    if (msg.maxNodes) planner.setMaxNodes(msg.maxNodes);
    let s = rbc.rankedBoard(msg.seed, msg.rank);
    postMessage({ type: 'state', state: s, text: 'start' });
    for (let n = 1; n <= 60; n++) {
      const c = planner.choose(s);
      if (!c) return postMessage({ type: 'end', result: 'White has no move: loss' });
      const [mv, , win] = c.split(' ');
      const [f, t] = mv.split('-').map(Number);
      s = rbc.play(s, f, t);
      let text = `${n}. ${rbc.squareName(f)}-${rbc.squareName(t)}` + (win ? ` (win in ${win})` : '');
      if (!s.split(' ')[0].includes('k') || rbc.legalMoves(s) === '') {
        postMessage({ type: 'state', state: s, text });
        return postMessage({ type: 'end', result: `White wins in ${n}` });
      }
      const [rf, rt] = rbc.engineMove(s, 3, 5, 5).split('-').map(Number);
      s = rbc.play(s, rf, rt);
      planner.observe(f, t, rf, rt);
      postMessage({ type: 'state', state: s, text: text + ` ${rbc.squareName(rf)}-${rbc.squareName(rt)}`, info: planner.lastInfo() });
    }
    postMessage({ type: 'end', result: 'move limit' });
  }
};
