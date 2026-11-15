// Node check of the wasm build against the test vectors (see README: "wasm").
//   node js/test.cjs <dir with rbc.js from wasm-bindgen --target nodejs>
const fs = require('fs'), path = require('path');
const rbc = require(path.resolve(process.argv[2] || 'pkg-node', 'rbc.js'));
const data = f => fs.readFileSync(path.join(__dirname, '..', 'tests', 'data', f), 'utf8').trim().split('\n');
let fail = 0;
const check = (ok, msg) => { if (!ok) { fail++; if (fail < 5) console.log('FAIL', msg); } };

// generator
for (const l of data('gen.txt').filter(l => l.startsWith('ranked'))) {
  const [, seed, rank, board] = l.split(' ');
  check(rbc.rankedBoard(+seed, +rank).split(' ')[0] === board.replace(/\//g, ''), l);
}
// legal moves and engine moves on the first 150 positions
const ps = data('pos.txt').slice(0, 150);
let t0 = Date.now();
for (const l of ps) {
  const f = l.split(' | ');
  check(rbc.legalMoves(f[0]) === f[2].replace(/k\d/g, '').replace(/^-$/, ''), 'legal ' + f[0]);
  const d = f[7].split(' ');
  const want = d.length > 3 ? d[3].split(':')[0].replace(/k\d/, '') : '';
  check(rbc.engineMove(f[0], +d[0], +d[1], +d[2]) === want, 'engine ' + f[0]);
}
console.log(`engine: ${ps.length} positions, ${((Date.now() - t0) / ps.length).toFixed(1)} ms each (incl. legal moves)`);

// one short game: planner (node-limited) against the engine
let s = rbc.rankedBoard(5000, 0), moves = 0, result = 'unfinished';
const planner = new rbc.Planner(30);
planner.setMaxNodes(300);
t0 = Date.now();
for (; moves < 40;) {
  const c = planner.choose(s);
  if (!c) { result = 'LOSS'; break; }
  const [f, t] = c.split(' ')[0].split('-').map(Number);
  s = rbc.play(s, f, t); moves++;
  if (!s.split(' ')[0].includes('k')) { result = 'WIN'; break; }
  const r = rbc.engineMove(s, 3, 5, 5);
  if (!r || rbc.legalMoves(s) === '') { result = 'WIN'; break; }
  const [rf, rt] = r.split('-').map(Number);
  s = rbc.play(s, rf, rt);
  planner.observe(f, t, rf, rt);
}
console.log(`game rank 0 seed 5000: ${result} in ${moves} moves, ${((Date.now() - t0) / 1000).toFixed(0)}s; ${planner.lastInfo()}`);
check(result === 'WIN', 'game');
console.log(fail ? `${fail} FAILURES` : 'all wasm checks passed');
process.exit(fail ? 1 : 0);
