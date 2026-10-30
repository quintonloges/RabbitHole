/** Constants consumed by the fixed Atlantic City game, not a configurable casino rules interface.
 * Other invariants are implemented by the engine and described in RULE_SUMMARY.
 * Strategy tables must be reverified before changing any rule.
 * References and verification notes: docs/blackjack-lab.md.
 */
export const RULES = {
  decks: 8,
  blackjackPayout: [3, 2],
  insurancePayout: [2, 1],
  doubleAfterSplit: true,
  lateSurrender: true,
  maxHands: 4,
  minimumBet: 500,
  maximumBet: 50_000,
  betIncrement: 100,
  startingBankroll: 50_000,
  // Reshuffle BETWEEN rounds. Keep ample cards for all four player hands + dealer.
  shuffleBelow: 104,
} as const;

export const RULESET_NAME = "Atlantic City";
export const RULESET_SUMMARY = `${RULESET_NAME} · ${RULES.decks} decks · S17 · 3:2 blackjack · double after split · late surrender · dealer peek`;
export const RULESET_NOTE = "This lab teaches classic Atlantic City blackjack. Casino and online tables can vary: check the deck count, blackjack payout, soft-17 rule, surrender availability, and split limits before using this chart.";

export const RULE_SUMMARY = [
  `${RULES.decks} standard 52-card decks. Dealer hits 16 or less and stands on every 17, including soft 17 (S17).`,
  "American hole card: dealer checks for blackjack with a 10-value card or Ace showing. Insurance is offered before the Ace check.",
  "Natural blackjack pays 3:2; ordinary wins pay 1:1; ties push. Insurance costs half the original bet and pays 2:1.",
  "Double any initial two-card hand below 21, including after a non-Ace split (DAS). Take exactly one card, then stand.",
  "Late surrender on your original two cards only, after blackjack is ruled out. Half your wager is returned.",
  "Split equal-value cards up to three times for four hands total, except aces. Each new hand requires another matching wager.",
  "Split aces get one card each: no hitting, doubling, or resplitting. A split 21 is an ordinary 21.",
  "Virtual table: $5–$500 opening wager, in $1 steps. Extra split/double bets may exceed the opening table maximum. No side bets beyond insurance.",
] as const;

export function money(cents: number) {
  return `$${(cents / 100).toLocaleString("en-US", { minimumFractionDigits: cents % 100 ? 2 : 0, maximumFractionDigits: 2 })}`;
}
