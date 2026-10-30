import { test, expect } from '@playwright/test';
import { type Rank, cardValue, createShoe, exampleCards, isBlackjack, orderedShoe, score } from '../../apps/web/lib/blackjack/cards';
import { type Game, act, canInsure, deal, dealerStep, insuranceDecision, legalActions, newGame, roundTotals, validBet } from '../../apps/web/lib/blackjack/engine';
import { teachingShoe } from '../../apps/web/lib/blackjack/lessons';

function opening(ranks: Rank[], bet = 1000, bankroll = 50_000) { return deal(newGame(teachingShoe(ranks), bankroll), bet); }
function finish(game: Game) {
  let next = game;
  for (let i = 0; i < 30 && next.phase === 'dealer'; i++) next = dealerStep(next);
  expect(next.phase).toBe('settled');
  return next;
}

test('blackjack scoring: hard, soft, several aces and busts', () => {
  for (const [ranks, total, soft, bust] of [
    [['K', '6'], 16, false, false], [['A', '6'], 17, true, false],
    [['A', '6', '9'], 16, false, false], [['A', 'A', '9'], 21, true, false],
    [['A', 'A', '9', 'K'], 21, false, false], [['A', 'A', 'A', '8'], 21, true, false],
    [['10', '8', '4'], 22, false, true], [['A', 'K'], 21, true, false],
  ] as [Rank[], number, boolean, boolean][]) expect(score(exampleCards(ranks))).toEqual({ total, soft, bust });
});

test('blackjack is only an original two-card 21', () => {
  expect(isBlackjack(exampleCards(['A', 'Q']))).toBe(true);
  expect(isBlackjack(exampleCards(['A', 'Q']), true)).toBe(false);
  expect(isBlackjack(exampleCards(['7', '5', '9']))).toBe(false);
});

test('shoe contains six complete decks and shuffling preserves every physical card', () => {
  const cards = orderedShoe();
  const shuffled = createShoe(() => .4);
  expect(cards).toHaveLength(312);
  expect(new Set(cards.map(card => card.id)).size).toBe(312);
  for (const rank of ['2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K', 'A']) expect(cards.filter(card => card.rank === rank)).toHaveLength(24);
  expect(cards.filter(card => cardValue(card) === 10)).toHaveLength(96);
  expect(shuffled.map(card => card.id).sort()).toEqual(cards.map(card => card.id).sort());
  expect(shuffled).not.toEqual(cards);
});

test('deal alternates player/upcard/player/hole and preserves its input', () => {
  const original = newGame(teachingShoe(['3', '6', '7', '10']));
  const snapshot = JSON.stringify(original);
  const game = deal(original, 1000);
  expect(game.hands[0].cards.map(card => card.rank)).toEqual(['3', '7']);
  expect(game.dealer.map(card => card.rank)).toEqual(['6', '10']);
  expect(game.cursor).toBe(4); expect(game.bankroll).toBe(49_000);
  expect(JSON.stringify(original)).toBe(snapshot);
});

test('dealer peeks under a ten and resolves blackjack before any action', () => {
  const game = opening(['9', 'K', '7', 'A']);
  expect(game.phase).toBe('settled'); expect(game.peeked).toBe(true);
  expect(game.hands[0].outcome).toBe('loss'); expect(game.bankroll).toBe(49_000);
  expect(legalActions(game)).toEqual([]);
});

test('natural pays 3:2 exactly, including an odd-dollar wager', () => {
  const game = opening(['A', '9', 'K', '7'], 500);
  expect(game.phase).toBe('settled'); expect(game.hands[0].outcome).toBe('blackjack');
  expect(game.hands[0].returned).toBe(1250); expect(game.bankroll).toBe(50_750);
});

test('both naturals push, including the Ace insurance offer', () => {
  const before = opening(['A', 'A', 'K', 'Q']);
  expect(before.phase).toBe('insurance'); expect(before.peeked).toBe(false);
  const game = insuranceDecision(before, false);
  expect(game.hands[0].outcome).toBe('push'); expect(game.bankroll).toBe(50_000);
});

test('insurance settles separately at 2:1 only after the Ace offer', () => {
  const initial = opening(['10', 'A', '9', 'K']);
  expect(initial.phase).toBe('insurance'); expect(legalActions(initial)).toEqual([]);
  expect(initial.bankroll).toBe(49_000); expect(initial.peeked).toBe(false);
  expect(roundTotals(initial)).toBeNull();
  const yes = insuranceDecision(initial, true);
  expect(yes.insurance).toBe(500); expect(yes.insuranceResult).toBe('won');
  expect(yes.bankroll).toBe(50_000); expect(yes.hands[0].outcome).toBe('loss');
  expect(roundTotals(yes)).toEqual({ wagered: 1500, returned: 1500, net: 0, insuranceReturned: 1500 });
  const no = insuranceDecision(initial, false);
  expect(no.bankroll).toBe(49_000); expect(no.insuranceResult).toBe('declined');
  expect(roundTotals(no)).toEqual({ wagered: 1000, returned: 0, net: -1000, insuranceReturned: 0 });
  expect(() => insuranceDecision(yes, true)).toThrow();
});

test('losing insurance does not end the main hand', () => {
  let game = insuranceDecision(opening(['10', 'A', '9', '8']), true);
  expect(game.phase).toBe('player'); expect(game.insuranceResult).toBe('lost');
  expect(game.bankroll).toBe(48_500); expect(game.peeked).toBe(true);
  expect(roundTotals(game)).toBeNull();
  game = finish(act(game, 'stand'));
  expect(game.hands[0].outcome).toBe('push'); expect(game.bankroll).toBe(49_500);
  expect(roundTotals(game)).toEqual({ wagered: 1500, returned: 1000, net: -500, insuranceReturned: 0 });
});

test('insurance on a natural is mathematically even money for either peek result', () => {
  for (const hole of ['K', '9'] as Rank[]) {
    const game = insuranceDecision(opening(['A', 'A', '10', hole]), true);
    expect(game.phase).toBe('settled'); expect(game.bankroll).toBe(51_000);
    expect(roundTotals(game)).toEqual({ wagered: 1500, returned: 2500, net: 1000, insuranceReturned: hole === 'K' ? 1500 : 0 });
  }
  expect(insuranceDecision(opening(['A', 'A', '10', '9']), false).bankroll).toBe(51_500);
});

test('cannot buy unaffordable insurance but may decline it', () => {
  const game = opening(['9', 'A', '7', 'K'], 1000, 1000);
  expect(canInsure(game)).toBe(false); expect(() => insuranceDecision(game, true)).toThrow();
  expect(insuranceDecision(game, false).bankroll).toBe(0);
});

test('dealer stands on soft 17 without consuming another card', () => {
  const game = finish(act(insuranceDecision(opening(['10', 'A', '7', '6', '10']), false), 'stand'));
  expect(game.cursor).toBe(4); expect(score(game.dealer)).toEqual({ total: 17, soft: true, bust: false });
  expect(game.hands[0].outcome).toBe('push'); expect(game.bankroll).toBe(50_000);
});

test('dealer hits soft 16, then keeps drawing when the Ace becomes hard', () => {
  let game = act(insuranceDecision(opening(['10', 'A', '8', '5', '10', '2']), false), 'stand');
  game = dealerStep(game);
  expect(score(game.dealer).total).toBe(16); expect(score(game.dealer).soft).toBe(false); expect(game.phase).toBe('dealer');
  game = dealerStep(game);
  expect(game.phase).toBe('settled'); expect(game.hands[0].outcome).toBe('push');
});

test('hit draws once and removes initial-only actions; stand passes to dealer', () => {
  const game = opening(['3', '6', '6', '10', '2', '8']);
  const next = act(game, 'hit');
  expect(next.cursor).toBe(5); expect(score(next.hands[0].cards).total).toBe(11);
  expect(legalActions(next)).toEqual(['hit', 'stand']);
  expect(finish(act(next, 'stand')).bankroll).toBe(51_000);
  expect(game.cursor).toBe(4);
});

test('player bust loses before a possible dealer bust, with no extra dealer draw', () => {
  const game = act(opening(['10', '6', '6', '10', 'K', 'K']), 'hit');
  expect(game.phase).toBe('settled'); expect(game.hands[0].outcome).toBe('bust');
  expect(game.cursor).toBe(5); expect(game.bankroll).toBe(49_000);
});

test('double debits one matching stake, draws exactly one, and stands', () => {
  const game = act(opening(['6', '6', '5', '10', '10', '8']), 'double');
  expect(game.phase).toBe('dealer'); expect(game.cursor).toBe(5);
  expect(game.hands[0].bet).toBe(2000); expect(game.hands[0].cards).toHaveLength(3);
  expect(game.bankroll).toBe(48_000); expect(legalActions(game)).toEqual([]);
  expect(finish(game).bankroll).toBe(52_000);
});

test('doubling any initial total is legal but a bust loses both stakes', () => {
  const game = act(opening(['10', '6', '6', '10', 'K']), 'double');
  expect(game.phase).toBe('settled'); expect(game.hands[0].outcome).toBe('bust'); expect(game.bankroll).toBe(48_000);
});

test('split hands play left to right, get their second card in turn, and permit DAS', () => {
  let game = act(opening(['8', '6', '8', '10', '3', '10', '2', 'K', '8']), 'split');
  expect(game.hands).toHaveLength(2); expect(game.hands[1].cards).toHaveLength(1);
  expect(game.activeHand).toBe(0); expect(game.bankroll).toBe(48_000);
  expect(legalActions(game)).toContain('double'); expect(legalActions(game)).not.toContain('surrender');
  game = act(game, 'hit');
  expect(game.activeHand).toBe(1); expect(game.hands[1].cards.map(card => card.rank)).toEqual(['8', '2']);
  game = finish(act(game, 'double'));
  expect(game.bankroll).toBe(53_000); expect(game.hands.map(hand => hand.returned)).toEqual([2000, 4000]);
});

test('split aces receive one card each with no hit, double, or resplit, and 21 pays 1:1', () => {
  const game = act(opening(['A', '6', 'A', '10', 'K', 'A', '8']), 'split');
  expect(game.phase).toBe('dealer'); expect(legalActions(game)).toEqual([]);
  expect(game.hands.every(hand => hand.splitAces && hand.fromSplit && hand.status === 'stood')).toBe(true);
  expect(game.hands.map(hand => hand.cards.length)).toEqual([2, 2]);
  const result = finish(game);
  expect(result.hands.map(hand => hand.outcome)).toEqual(['win', 'win']); expect(result.bankroll).toBe(52_000);
});

test('split 21 pushes an ordinary dealer 21 and is not a natural', () => {
  const game = finish(act(opening(['A', '6', 'A', '5', 'K', '9', '10']), 'split'));
  expect(game.hands[0].outcome).toBe('push'); expect(game.hands[1].outcome).toBe('loss'); expect(game.bankroll).toBe(49_000);
});

test('equal-value face cards may split', () => {
  const game = opening(['J', '6', 'Q', '10', '2']);
  expect(legalActions(game)).toContain('split'); expect(act(game, 'split').hands).toHaveLength(2);
});

test('resplitting stops at four hands and cannot debit a fifth stake', () => {
  let game = opening(['8', '6', '8', '10', '8', '8', '8', '3', '4', '5', '8']);
  for (let i = 0; i < 3; i++) game = act(game, 'split');
  expect(game.hands).toHaveLength(4); expect(game.bankroll).toBe(46_000);
  expect(legalActions(game)).not.toContain('split'); expect(() => act(game, 'split')).toThrow();
  while (game.phase === 'player') game = act(game, 'stand');
  expect(finish(game).bankroll).toBe(54_000);
});

test('late surrender returns exactly half and is blocked after hit or split', () => {
  const game = opening(['10', '10', '6', '7']);
  expect(game.peeked).toBe(true);
  expect(act(game, 'surrender').bankroll).toBe(49_500);
  const afterHit = act(opening(['2', '10', '4', '7', '2']), 'hit');
  expect(() => act(afterHit, 'surrender')).toThrow();
  expect(() => act(act(opening(['8', '6', '8', '10', '2']), 'split'), 'surrender')).toThrow();
});

for (const [name, ranks, outcome, balance] of [
  ['win', ['10', '10', '9', '8'], 'win', 51_000],
  ['loss', ['10', '10', '7', '8'], 'loss', 49_000],
  ['push', ['10', '10', '8', '8'], 'push', 50_000],
] as [string, Rank[], string, number][]) test(`ordinary ${name} credits the correct amount`, () => {
  const game = finish(act(opening(ranks), 'stand'));
  expect(game.hands[0].outcome).toBe(outcome); expect(game.bankroll).toBe(balance);
  expect(() => dealerStep(game)).toThrow(); expect(() => act(game, 'stand')).toThrow();
});

test('invalid wagers and unaffordable extra bets never create negative balances', () => {
  for (const bet of [-1000, 0, 400, 550, 50_100, NaN, Infinity, 1000.5]) {
    expect(validBet(bet, 50_000)).toBe(false); expect(() => deal(newGame(), bet)).toThrow();
  }
  expect(() => opening(['8', '6', '8', '10'], 1000, 500)).toThrow();
  const game = opening(['8', '6', '8', '10'], 1000, 1000);
  expect(game.bankroll).toBe(0); expect(legalActions(game)).toEqual(['hit', 'stand', 'surrender']);
  expect(() => act(game, 'split')).toThrow(); expect(() => act(game, 'double')).toThrow(); expect(() => deal(game, 500)).toThrow();
  expect(game.bankroll).toBe(0);
});

test('the next round settles its natural once, even after an already settled round', () => {
  let game = opening(['10', '10', '8', '8', 'A', '9', 'K', '7']);
  game = finish(act(game, 'stand'));
  game = deal(game, 1000);
  expect(game.phase).toBe('settled'); expect(game.hands[0].outcome).toBe('blackjack'); expect(game.bankroll).toBe(51_500);
});

test('shoe is reused, then reshuffled between rounds at the reserve threshold', () => {
  const original = newGame(teachingShoe(['10', '10', '8', '8']));
  let game = finish(act(deal(original, 1000), 'stand'));
  expect(game.shoe).toBe(original.shoe); expect(game.cursor).toBe(4);
  game = { ...game, cursor: 250 };
  const next = deal(game, 1000, () => .5);
  expect(next.cursor).toBe(4); expect(next.shoeNumber).toBe(1); expect(next.shoe).toHaveLength(312);
});

test('an exhausted injected shoe fails atomically instead of drawing an impossible card', () => {
  const before = opening(['4', '6', '5', '10']);
  const game = { ...before, shoe: before.shoe.slice(0, 4) };
  expect(() => act(game, 'hit')).toThrow('exhausted'); expect(game.cursor).toBe(4); expect(game.hands[0].cards).toHaveLength(2);
});

test('many deterministic rounds conserve the ledger with no duplicate cards or illegal actions', () => {
  let seed = 129;
  const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 4294967296; };
  let game = newGame();
  for (let round = 0; round < 500; round++) {
    if (game.bankroll < 500) game = newGame();
    game = deal(game, 500, random);
    if (game.phase === 'insurance') game = insuranceDecision(game, canInsure(game) && random() < .5);
    let moves = 0;
    while (game.phase === 'player' && moves++ < 100) {
      const actions = legalActions(game);
      expect(actions.length).toBeGreaterThan(0);
      game = act(game, actions[Math.floor(random() * actions.length)]);
      expect(game.bankroll).toBeGreaterThanOrEqual(0); expect(Number.isSafeInteger(game.bankroll)).toBe(true);
    }
    game = finish(game);
    const risked = game.hands.reduce((sum, hand) => sum + hand.bet, 0) + game.insurance;
    const returned = game.hands.reduce((sum, hand) => sum + (hand.returned ?? 0), 0) + (game.insuranceResult === 'won' ? game.insurance * 3 : 0);
    expect(game.bankroll).toBe(game.roundStartBankroll - risked + returned);
    expect(roundTotals(game)).toMatchObject({ wagered: risked, returned, net: game.bankroll - game.roundStartBankroll });
    const dealt = [...game.dealer, ...game.hands.flatMap(hand => hand.cards)];
    expect(new Set(dealt.map(card => card.id)).size).toBe(dealt.length);
    expect(game.hands.length).toBeLessThanOrEqual(4);
  }
});
