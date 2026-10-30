import { type Card, cardValue, pairValue, score } from "./cards";
import type { Action } from "./engine";

export const UPCARDS = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11] as const;
export type Cell = "H" | "S" | "D" | "Ds" | "P" | "R";
type Row = readonly [Cell, Cell, Cell, Cell, Cell, Cell, Cell, Cell, Cell, Cell];
export type StrategyTable = Readonly<Record<number, Row>>;

/** Total-dependent basic strategy: 4–8 decks, S17, DAS, late surrender, American peek.
 * Verified against Wizard of Odds' 4-decks text/chart and surrender table, 2026-10-29.
 * Columns: 2 3 4 5 6 7 8 9 10 A. D falls back to hit; Ds to stand; R to hit.
 * Pair recommendations take precedence over total recommendations when splitting is legal.
 * These are NOT composition-dependent exceptions or card-counting indices.
 */
export const HARD: StrategyTable = {
  4: ["H", "H", "H", "H", "H", "H", "H", "H", "H", "H"],
  5: ["H", "H", "H", "H", "H", "H", "H", "H", "H", "H"],
  6: ["H", "H", "H", "H", "H", "H", "H", "H", "H", "H"],
  7: ["H", "H", "H", "H", "H", "H", "H", "H", "H", "H"],
  8: ["H", "H", "H", "H", "H", "H", "H", "H", "H", "H"],
  9: ["H", "D", "D", "D", "D", "H", "H", "H", "H", "H"],
  10: ["D", "D", "D", "D", "D", "D", "D", "D", "H", "H"],
  11: ["D", "D", "D", "D", "D", "D", "D", "D", "D", "H"],
  12: ["H", "H", "S", "S", "S", "H", "H", "H", "H", "H"],
  13: ["S", "S", "S", "S", "S", "H", "H", "H", "H", "H"],
  14: ["S", "S", "S", "S", "S", "H", "H", "H", "H", "H"],
  15: ["S", "S", "S", "S", "S", "H", "H", "H", "R", "H"],
  16: ["S", "S", "S", "S", "S", "H", "H", "R", "R", "R"],
  17: ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S"],
  18: ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S"],
  19: ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S"],
  20: ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S"],
  21: ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S"],
};

export const SOFT: StrategyTable = {
  12: ["H", "H", "H", "H", "D", "H", "H", "H", "H", "H"],
  13: ["H", "H", "H", "D", "D", "H", "H", "H", "H", "H"],
  14: ["H", "H", "H", "D", "D", "H", "H", "H", "H", "H"],
  15: ["H", "H", "D", "D", "D", "H", "H", "H", "H", "H"],
  16: ["H", "H", "D", "D", "D", "H", "H", "H", "H", "H"],
  17: ["H", "D", "D", "D", "D", "H", "H", "H", "H", "H"],
  18: ["S", "Ds", "Ds", "Ds", "Ds", "S", "S", "H", "H", "H"],
  19: ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S"],
  20: ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S"],
  21: ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S"],
};

export const PAIRS: StrategyTable = {
  2: ["P", "P", "P", "P", "P", "P", "H", "H", "H", "H"],
  3: ["P", "P", "P", "P", "P", "P", "H", "H", "H", "H"],
  4: ["H", "H", "H", "P", "P", "H", "H", "H", "H", "H"],
  5: ["D", "D", "D", "D", "D", "D", "D", "D", "H", "H"],
  6: ["P", "P", "P", "P", "P", "H", "H", "H", "H", "H"],
  7: ["P", "P", "P", "P", "P", "P", "H", "H", "H", "H"],
  8: ["P", "P", "P", "P", "P", "P", "P", "P", "P", "P"],
  9: ["P", "P", "P", "P", "P", "S", "P", "P", "S", "S"],
  10: ["S", "S", "S", "S", "S", "S", "S", "S", "S", "S"],
  11: ["P", "P", "P", "P", "P", "P", "P", "P", "P", "P"],
};

export const ACTION_LABELS: Record<Action, string> = { hit: "Hit", stand: "Stand", double: "Double down", split: "Split", surrender: "Surrender" };
export const CELL_LABELS: Record<Cell, string> = { H: "Hit", S: "Stand", D: "Double, otherwise hit", Ds: "Double, otherwise stand", P: "Split, otherwise use the total", R: "Surrender, otherwise hit" };
export type Decision = { cards: readonly Card[]; dealerUpcard: Card; legal: readonly Action[] };

export function strategyCell(decision: Decision): { cell: Cell; family: "hard" | "soft" | "pair" } {
  const column = UPCARDS.indexOf(cardValue(decision.dealerUpcard) as typeof UPCARDS[number]);
  if (column < 0) throw new Error("Invalid dealer upcard.");
  const pair = pairValue(decision.cards);
  if (pair !== null && decision.legal.includes("split")) return { cell: PAIRS[pair][column], family: "pair" };
  const hand = score(decision.cards);
  const family = hand.soft ? "soft" : "hard";
  const row = (hand.soft ? SOFT : HARD)[hand.total];
  if (!row) throw new Error("No strategy decision exists for this total.");
  return { cell: row[column], family };
}

export function recommend(decision: Decision): Action | null {
  if (!decision.legal.length || score(decision.cards).total >= 21) return null;
  const { cell } = strategyCell(decision);
  const preference: Record<Cell, readonly Action[]> = {
    H: ["hit"], S: ["stand"], D: ["double", "hit"], Ds: ["double", "stand"], P: ["split"], R: ["surrender", "hit"],
  };
  return preference[cell].find(action => decision.legal.includes(action)) ?? null;
}
