/* tslint:disable */
/* eslint-disable */

/**
 * The planner. Keep one per game: it remembers its plan between moves.
 */
export class Planner {
    free(): void;
    [Symbol.dispose](): void;
    /**
     * Our move in `state` (our turn): "from-to", plus " win N" if a forced win in N moves was
     * found. "" if we have no move.
     */
    choose(state: string): string;
    /**
     * Statistics of the last choose().
     */
    lastInfo(): string;
    /**
     * `seconds`: thinking time per move.
     */
    constructor(seconds: number);
    /**
     * Tell the planner what was played: our move and the opponent's reply.
     */
    observe(our_from: number, our_to: number, reply_from: number, reply_to: number): void;
    /**
     * The current plan as "f-t f-t ...": our move, the expected reply, our next move, and so on.
     * Empty if there is none. Its first move is what choose() plays unless it finds a faster win.
     */
    planLine(): string;
    /**
     * Also stop after this many searched positions per move (0: no limit).
     */
    setMaxNodes(n: number): void;
    /**
     * Restore a plan saved with planLine(), e.g. into a new Planner.
     */
    setPlanLine(line: string): void;
}

/**
 * The daily game; `date` is yyyyMMdd as a number, e.g. 20261115.
 */
export function dailyBoard(date: number): string;

/**
 * The move the game's engine plays here, "from-to", or "" if it has none. The game uses 3, 5, 5.
 */
export function engineMove(state: string, weak: number, strong: number, end: number): string;

/**
 * Starting position of a free-play game at this difficulty.
 */
export function freeplayBoard(seed: number, difficulty: number): string;

/**
 * Legal moves of the side to move, "from-to from-to ...". Empty: the side to move has lost.
 */
export function legalMoves(state: string): string;

/**
 * Play a legal move and return the new state.
 */
export function play(state: string, from: number, to: number): string;

/**
 * Starting position of a ranked game at `rank` (the game's generator, same seed same board).
 */
export function rankedBoard(seed: number, rank: number): string;

/**
 * Algebraic name of a square index, e.g. 52 -> "e2".
 */
export function squareName(i: number): string;

/**
 * The weekly game for the game's weekly seed.
 */
export function weeklyBoard(seed: number): string;

export type InitInput = RequestInfo | URL | Response | BufferSource | WebAssembly.Module;

export interface InitOutput {
    readonly memory: WebAssembly.Memory;
    readonly __wbg_planner_free: (a: number, b: number) => void;
    readonly dailyBoard: (a: number) => [number, number];
    readonly engineMove: (a: number, b: number, c: number, d: number, e: number) => [number, number, number, number];
    readonly freeplayBoard: (a: number, b: number) => [number, number];
    readonly legalMoves: (a: number, b: number) => [number, number, number, number];
    readonly planner_choose: (a: number, b: number, c: number) => [number, number, number, number];
    readonly planner_lastInfo: (a: number) => [number, number];
    readonly planner_new: (a: number) => number;
    readonly planner_observe: (a: number, b: number, c: number, d: number, e: number) => void;
    readonly planner_planLine: (a: number) => [number, number];
    readonly planner_setMaxNodes: (a: number, b: number) => void;
    readonly planner_setPlanLine: (a: number, b: number, c: number) => [number, number];
    readonly play: (a: number, b: number, c: number, d: number) => [number, number, number, number];
    readonly rankedBoard: (a: number, b: number) => [number, number];
    readonly squareName: (a: number) => [number, number];
    readonly weeklyBoard: (a: number) => [number, number];
    readonly __wbindgen_externrefs: WebAssembly.Table;
    readonly __wbindgen_free: (a: number, b: number, c: number) => void;
    readonly __wbindgen_malloc: (a: number, b: number) => number;
    readonly __wbindgen_realloc: (a: number, b: number, c: number, d: number) => number;
    readonly __externref_table_dealloc: (a: number) => void;
    readonly __wbindgen_start: () => void;
}

export type SyncInitInput = BufferSource | WebAssembly.Module;

/**
 * Instantiates the given `module`, which can either be bytes or
 * a precompiled `WebAssembly.Module`.
 *
 * @param {{ module: SyncInitInput }} module - Passing `SyncInitInput` directly is deprecated.
 *
 * @returns {InitOutput}
 */
export function initSync(module: { module: SyncInitInput } | SyncInitInput): InitOutput;

/**
 * If `module_or_path` is {RequestInfo} or {URL}, makes a request and
 * for everything else, calls `WebAssembly.instantiate` directly.
 *
 * @param {{ module_or_path: InitInput | Promise<InitInput> }} module_or_path - Passing `InitInput` directly is deprecated.
 *
 * @returns {Promise<InitOutput>}
 */
export default function __wbg_init (module_or_path?: { module_or_path: InitInput | Promise<InitInput> } | InitInput | Promise<InitInput>): Promise<InitOutput>;
