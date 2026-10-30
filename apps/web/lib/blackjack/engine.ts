import { type Card, cardValue, createShoe, isBlackjack, pairValue, score } from "./cards";
import { RULES } from "./rules";

export type Action = "hit" | "stand" | "double" | "split" | "surrender";
export type Phase = "betting" | "insurance" | "player" | "dealer" | "settled";
export type Outcome = "blackjack" | "win" | "loss" | "push" | "bust" | "surrender";
export type Hand = {
  id: number;
  cards: Card[];
  bet: number;
  fromSplit: boolean;
  splitAces: boolean;
  status: "playing" | "waiting" | "stood" | "busted" | "surrendered";
  outcome?: Outcome;
  returned?: number;
};
export type Game = {
  phase: Phase;
  bankroll: number;
  roundStartBankroll: number;
  shoe: Card[];
  cursor: number;
  shoeNumber: number;
  dealer: Card[];
  hands: Hand[];
  activeHand: number;
  nextHandId: number;
  insurance: number;
  insuranceResult: "none" | "declined" | "won" | "lost";
  peeked: boolean;
};

export function newGame(shoe: Card[] = [], bankroll: number = RULES.startingBankroll): Game {
  if (!Number.isSafeInteger(bankroll) || bankroll < 0) throw new Error("Invalid chip balance.");
  return { phase: "betting", bankroll, roundStartBankroll: bankroll, shoe: [...shoe], cursor: 0, shoeNumber: 0, dealer: [], hands: [], activeHand: 0, nextHandId: 1, insurance: 0, insuranceResult: "none", peeked: false };
}

export function legalHandActions(hand: Hand, bankroll: number, handCount: number): Action[] {
  if (hand.status !== "playing" || hand.splitAces || score(hand.cards).total >= 21) return [];
  const actions: Action[] = ["hit", "stand"];
  const twoCards = hand.cards.length === 2;
  if (twoCards && bankroll >= hand.bet && (!hand.fromSplit || RULES.doubleAfterSplit)) actions.push("double");
  if (twoCards && pairValue(hand.cards) !== null && bankroll >= hand.bet && handCount < RULES.maxHands) actions.push("split");
  if (twoCards && !hand.fromSplit && RULES.lateSurrender) actions.push("surrender");
  return actions;
}

export function legalActions(game: Game): Action[] {
  return game.phase === "player" ? legalHandActions(game.hands[game.activeHand], game.bankroll, game.hands.length) : [];
}

/** Insurance returns its stake plus the 2:1 profit, separately from every main hand. */
function insuranceReturn(game: Game): number {
  return game.insuranceResult === "won" ? game.insurance * (1 + RULES.insurancePayout[0] / RULES.insurancePayout[1]) : 0;
}

/** Completed-round totals include every split/double stake and the separate insurance bet. */
export function roundTotals(game: Game) {
  if (game.phase !== "settled") return null;
  const insuranceReturned = insuranceReturn(game);
  const wagered = game.hands.reduce((sum, hand) => sum + hand.bet, 0) + game.insurance;
  const returned = game.hands.reduce((sum, hand) => sum + (hand.returned ?? 0), 0) + insuranceReturned;
  return { wagered, returned, net: returned - wagered, insuranceReturned };
}

function clone(game: Game): Game {
  return { ...game, dealer: [...game.dealer], hands: game.hands.map(hand => ({ ...hand, cards: [...hand.cards] })) };
}

function draw(game: Game): Card {
  const card = game.shoe[game.cursor++];
  if (!card) throw new Error("The shoe is exhausted. Reset the table to start a fresh shoe.");
  return card;
}

function makeHand(game: Game, cards: Card[], bet: number, fromSplit = false, splitAces = false): Hand {
  return { id: game.nextHandId++, cards, bet, fromSplit, splitAces, status: cards.length === 1 ? "waiting" : "playing" };
}

/** All stakes are debited on placement; settlement credits stake + profit exactly once. */
function settle(game: Game) {
  if (game.phase === "settled") return;
  const dealer = score(game.dealer);
  const dealerNatural = isBlackjack(game.dealer);
  for (const hand of game.hands) {
    const value = score(hand.cards);
    let returned = 0;
    let outcome: Outcome;
    if (hand.status === "surrendered") { outcome = "surrender"; returned = hand.bet / 2; }
    else if (value.bust) outcome = "bust";
    else if (isBlackjack(hand.cards, hand.fromSplit)) {
      outcome = dealerNatural ? "push" : "blackjack";
      returned = dealerNatural ? hand.bet : hand.bet + hand.bet * RULES.blackjackPayout[0] / RULES.blackjackPayout[1];
    } else if (dealerNatural) outcome = "loss";
    else if (dealer.bust || value.total > dealer.total) { outcome = "win"; returned = hand.bet * 2; }
    else if (value.total === dealer.total) { outcome = "push"; returned = hand.bet; }
    else outcome = "loss";
    hand.outcome = outcome;
    hand.returned = returned;
    game.bankroll += returned;
  }
  game.phase = "settled";
}

function finishPeek(game: Game) {
  const upcard = cardValue(game.dealer[0]);
  game.peeked = upcard === 10 || upcard === 11;
  const dealerNatural = isBlackjack(game.dealer);
  if (game.insurance > 0) {
    game.insuranceResult = dealerNatural ? "won" : "lost";
    game.bankroll += insuranceReturn(game);
  }
  if (dealerNatural || isBlackjack(game.hands[0].cards)) settle(game);
  else game.phase = "player";
}

export function validBet(bet: number, bankroll: number) {
  return Number.isSafeInteger(bet) && bet >= RULES.minimumBet && bet <= RULES.maximumBet && bet % RULES.betIncrement === 0 && bet <= bankroll;
}

/** Deal order is player, dealer upcard, player, dealer hole card. Tests inject a full ordered shoe. */
export function deal(game: Game, bet: number, random: () => number = Math.random): Game {
  if (game.phase !== "betting" && game.phase !== "settled") throw new Error("Finish this round before dealing again.");
  if (!validBet(bet, game.bankroll)) throw new Error("Choose a whole-dollar wager from $5 to $500 within your available chips.");
  const next = clone(game);
  if (next.shoe.length - next.cursor < RULES.shuffleBelow) {
    next.shoe = createShoe(random); next.cursor = 0; next.shoeNumber++;
  }
  next.roundStartBankroll = next.bankroll;
  next.bankroll -= bet;
  next.insurance = 0; next.insuranceResult = "none"; next.peeked = false;
  next.phase = "player";
  next.hands = []; next.dealer = []; next.activeHand = 0;
  const first = draw(next);
  next.dealer.push(draw(next));
  next.hands.push(makeHand(next, [first, draw(next)], bet));
  next.dealer.push(draw(next));
  if (next.dealer[0].rank === "A") next.phase = "insurance";
  else finishPeek(next);
  return next;
}

export function canInsure(game: Game) { return game.phase === "insurance" && game.bankroll >= game.hands[0].bet / 2; }

export function insuranceDecision(game: Game, accept: boolean): Game {
  if (game.phase !== "insurance") throw new Error("Insurance is only offered before the dealer's Ace peek.");
  if (accept && !canInsure(game)) throw new Error("Not enough available chips for insurance.");
  const next = clone(game);
  if (accept) { next.insurance = next.hands[0].bet / 2; next.bankroll -= next.insurance; }
  else next.insuranceResult = "declined";
  finishPeek(next);
  return next;
}

function advance(game: Game) {
  while (game.activeHand < game.hands.length) {
    const hand = game.hands[game.activeHand];
    if (hand.status === "waiting") { hand.cards.push(draw(game)); hand.status = "playing"; }
    if (hand.status === "playing" && score(hand.cards).total < 21 && !hand.splitAces) return;
    if (hand.status === "playing") hand.status = score(hand.cards).bust ? "busted" : "stood";
    game.activeHand++;
  }
  game.phase = "dealer";
  // No drawing is needed when every player hand already lost or surrendered.
  if (game.hands.every(hand => hand.status === "busted" || hand.status === "surrendered")) settle(game);
}

export function act(game: Game, action: Action): Game {
  if (!legalActions(game).includes(action)) throw new Error("That action is not available for this hand.");
  const next = clone(game);
  const hand = next.hands[next.activeHand];
  if (action === "hit") {
    hand.cards.push(draw(next));
    if (score(hand.cards).bust) hand.status = "busted";
  } else if (action === "stand") hand.status = "stood";
  else if (action === "surrender") hand.status = "surrendered";
  else if (action === "double") {
    next.bankroll -= hand.bet;
    hand.bet *= 2;
    hand.cards.push(draw(next));
    hand.status = score(hand.cards).bust ? "busted" : "stood";
  } else if (action === "split") {
    next.bankroll -= hand.bet;
    const aces = hand.cards[0].rank === "A";
    const left = makeHand(next, [hand.cards[0]], hand.bet, true, aces);
    const right = makeHand(next, [hand.cards[1]], hand.bet, true, aces);
    next.hands.splice(next.activeHand, 1, left, right);
    // Play the left hand fully before dealing the next, as at a physical table.
    if (aces) {
      left.cards.push(draw(next)); right.cards.push(draw(next));
      left.status = "stood"; right.status = "stood";
    }
  }
  advance(next);
  return next;
}

/** One observable dealer step. S17 means even A+6 stops drawing. */
export function dealerStep(game: Game): Game {
  if (game.phase !== "dealer") throw new Error("It is not the dealer's turn.");
  const next = clone(game);
  if (score(next.dealer).total < 17) next.dealer.push(draw(next));
  if (score(next.dealer).total >= 17) settle(next);
  return next;
}
