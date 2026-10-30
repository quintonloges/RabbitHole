import { type Card, type Rank, exampleCards, pairValue, score, shuffle } from "./cards";
import { type Hand, legalHandActions } from "./engine";
import { type Decision, recommend } from "./strategy";

export const DRILLS = ["All Hands", "Hard Totals", "Soft Totals", "Splitting", "Doubling", "Surrender"] as const;
export type Drill = typeof DRILLS[number];
export type Scenario = { id: string; hand: Hand; dealer: Card; bankroll: number };

export function scenario(ranks: readonly Rank[], upcard: Rank, options: { fromSplit?: boolean; bankroll?: number } = {}): Scenario {
  return {
    id: `${ranks.join("-")}:${upcard}:${options.fromSplit ? "split" : "original"}:${options.bankroll ?? 50_000}`,
    hand: { id: 1, cards: exampleCards(ranks), bet: 1000, status: "playing", fromSplit: options.fromSplit ?? false, splitAces: false },
    dealer: { id: "dealer-example", rank: upcard, suit: "spades" }, bankroll: options.bankroll ?? 50_000,
  };
}

export function decisionFor(item: Scenario): Decision {
  return { cards: item.hand.cards, dealerUpcard: item.dealer, legal: legalHandActions(item.hand, item.bankroll, item.hand.fromSplit ? 2 : 1) };
}

/** A shuffled bag of all two-card value combinations, plus subsequent and split decisions.
 * Draw without replacement: the bag is exhausted before any scenario repeats.
 */
export function practicePool(category: Drill): Scenario[] {
  const ranks: Rank[] = ["2", "3", "4", "5", "6", "7", "8", "9", "10", "A"];
  const pool: Scenario[] = [];
  for (let a = 0; a < ranks.length; a++) for (let b = a; b < ranks.length; b++) {
    if (score(exampleCards([ranks[a], ranks[b]])).total >= 21) continue;
    for (const dealer of ranks) pool.push(scenario([ranks[a], ranks[b]], dealer));
  }
  for (const cards of [["2", "3", "6"], ["A", "2", "5"], ["10", "2", "4"], ["A", "6", "9"], ["2", "4", "6"], ["A", "A", "5"], ["3", "4", "8"]] as Rank[][]) {
    for (const dealer of ranks) pool.push(scenario(cards, dealer));
  }
  for (const cards of [["8", "3"], ["8", "8"], ["6", "10"], ["4", "7"], ["9", "9"]] as Rank[][]) {
    for (const dealer of ranks) pool.push(scenario(cards, dealer, { fromSplit: true }));
  }
  return pool.filter(item => {
    const value = score(item.hand.cards);
    const pair = pairValue(item.hand.cards);
    const decision = decisionFor(item);
    switch (category) {
      case "Hard Totals": return !value.soft && pair === null;
      case "Soft Totals": return value.soft && pair === null;
      case "Splitting": return pair !== null;
      // Include close counterexamples: these drills must teach when NOT to double/surrender.
      case "Doubling": return decision.legal.includes("double") && (value.total >= 9 && value.total <= 11 || value.soft);
      case "Surrender": return !value.soft && value.total >= 15 && value.total <= 17 && decision.legal.includes("surrender");
      default: return recommend(decision) !== null;
    }
  });
}

export function practiceBag(category: Drill, random: () => number = Math.random) { return shuffle(practicePool(category), random); }
