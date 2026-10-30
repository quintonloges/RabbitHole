import { cardValue, handLabel, pairValue, score } from "./cards";
import type { Action } from "./engine";
import { ACTION_LABELS, type Decision, recommend, strategyCell } from "./strategy";

export type Feedback = { correct: boolean; title: string; explanation: string; context: string };

export function explain(decision: Decision): string {
  const action = recommend(decision);
  if (!action) return "This hand is complete; there is no further player decision.";
  const hand = score(decision.cards);
  const pair = pairValue(decision.cards);
  const up = cardValue(decision.dealerUpcard);
  const dealer = up === 11 ? "Ace" : String(up);
  const { cell } = strategyCell(decision);
  if (cell === "R" && action === "hit") return `Surrender is unavailable, so hit hard ${hand.total} against ${dealer}. Standing loses more often in this matchup.`;
  if ((cell === "D" || cell === "Ds") && action !== "double") return `Doubling is unavailable with these cards or chips. ${action === "stand" ? "Keep your soft 18 and stand" : `Hit to improve your ${hand.soft ? "soft" : "hard"} ${hand.total}`} against ${dealer}.`;
  if (action === "split") {
    if (pair === 11) return "Two aces are a flexible 12 together. Split them to start two hands at 11; each receives just one more card.";
    if (pair === 8) return "Together, two 8s make a weak 16. Splitting gives each 8 a better starting point, even against a strong upcard.";
    if (pair === 4) return `Against ${dealer}, splitting 4s becomes worthwhile because you may double after the split.`;
    return `Split your ${pair}s against ${dealer}. Two separate starting hands have a better expected return here than playing the pair together.`;
  }
  if (action === "surrender") return `Hard ${hand.total} is badly placed against ${dealer}. After the dealer's blackjack check, giving up half the bet costs less on average than playing on.`;
  if (action === "double") return hand.soft
    ? `Your Ace keeps soft ${hand.total} flexible. Against the dealer's ${dealer}, adding a matching wager for one card is the stronger play.`
    : `Hard ${hand.total} is a strong starting total against ${dealer}. Doubling puts more chips on this favorable matchup, with exactly one card to come.`;
  if (action === "stand") {
    if (pair === 10) return "Keep your 20 together. Splitting gives up an already strong hand.";
    if (pair === 9 && up === 7) return "Your 18 already beats the dealer's common finishing total of 17. Keep the two 9s together against 7.";
    if (hand.total <= 16) return `The dealer's ${dealer} is vulnerable. With hard ${hand.total}, standing is preferable to risking a bust by hitting.`;
    return `Keep your ${hand.soft ? "soft" : "hard"} ${hand.total} against ${dealer}. Another card does not improve the expected result enough to justify it.`;
  }
  if (hand.soft) return `Soft ${hand.total} needs improvement against ${dealer}. You cannot bust on this next hit: the Ace can fall from 11 to 1.`;
  if (hand.total === 11 && up === 11) return "Against an Ace in this S17 game, hit 11. Keep the option of another draw instead of committing to just one card with a double.";
  if (hand.total === 12 && up <= 3) return `A dealer ${dealer} is not weak enough to justify standing on 12. Hitting has the better expected result.`;
  return `Improve hard ${hand.total} against ${dealer}. ${hand.total <= 11 ? "Your next card cannot bust this hand." : "The dealer's strong upcard makes standing too costly, despite the risk of busting."}`;
}

export function coach(decision: Decision, chosen: Action): Feedback {
  const best = recommend(decision);
  const correct = best === chosen;
  return {
    correct,
    title: best ? `${correct ? "Correct —" : "Best move:"} ${ACTION_LABELS[best]}.` : "Hand complete.",
    explanation: explain(decision),
    context: `${decision.cards.map(card => card.rank).join(" + ")} · ${handLabel(decision.cards)} vs ${decision.dealerUpcard.rank} · You chose ${ACTION_LABELS[chosen]}`,
  };
}

export function coachInsurance(accepted: boolean): Feedback {
  return { correct: !accepted, title: accepted ? "Best move: Decline insurance." : "Correct — Decline insurance.", explanation: "Insurance is a separate bet that the hole card is worth 10. Its 2:1 payout does not cover the odds in basic strategy; decline even when you have blackjack.", context: accepted ? "You took insurance. This advice concerns the separate insurance bet." : "You declined insurance." };
}
