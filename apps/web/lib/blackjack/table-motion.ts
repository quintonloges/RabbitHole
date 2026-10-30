import type { Action, Game } from "./engine";

export type TableMove = Action | "deal" | "insurance" | "dealer";
export type TableFrame = {
  visibleCards: readonly string[];
  hideHole: boolean;
  bankroll: number;
  message: string;
  duration: number;
};

const wagered = (game: Game) => game.hands.reduce((sum, hand) => sum + hand.bet, 0) + game.insurance;

/** Presentation only: reveal the engine's completed transition without changing its rules or ledger. */
export function tableMotion(before: Game, next: Game, move: TableMove): TableFrame[] {
  const frames: TableFrame[] = [];
  const visible = move === "deal" ? [] : [...before.dealer, ...before.hands.flatMap(hand => hand.cards)].map(card => card.id);
  let hideHole = move === "deal" || before.phase === "player" || before.phase === "insurance";
  // New stakes appear immediately; returns arrive with the completed result.
  const bankroll = before.bankroll - (wagered(next) - (move === "deal" ? 0 : wagered(before)));
  const frame = (message: string, duration: number) => frames.push({ visibleCards: [...visible], hideHole, bankroll, message, duration });

  if (move === "deal") {
    const opening = [next.hands[0].cards[0], next.dealer[0], next.hands[0].cards[1], next.dealer[1]];
    opening.forEach((card, index) => {
      visible.push(card.id);
      frame("Dealing…", index === opening.length - 1 ? 300 : 160);
    });
  } else {
    const message = move === "hit" ? "One more card…" : move === "double" ? "Doubling down · one final card…"
      : move === "split" ? "Splitting your hand…" : move === "insurance" ? "Dealer checks for blackjack…"
      : move === "surrender" ? "Surrendering the hand…" : move === "stand" ? "Standing…" : "Dealer draws…";
    if (move === "split") frame(message, 200);
    // Physical draw order also covers forced cards and advancing through split hands.
    const drawn = next.shoe.slice(before.cursor, next.cursor);
    for (const card of drawn) {
      visible.push(card.id);
      frame(message, 320);
    }
    if (!drawn.length && move !== "split") frame(move === "dealer" ? "Dealer stands." : message, move === "insurance" ? 360 : 200);
  }

  if (hideHole && (next.phase === "dealer" || next.phase === "settled")) {
    hideHole = false;
    frame("Dealer reveals the hole card…", 420);
  }
  if (next.phase === "settled") frame("Settling the round…", 240);
  return frames;
}
