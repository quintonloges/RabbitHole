import { test, expect } from '@playwright/test';
import { type Rank, exampleCards, score } from '../../apps/web/lib/blackjack/cards';
import { type Action, legalHandActions } from '../../apps/web/lib/blackjack/engine';
import { HARD, SOFT, PAIRS, UPCARDS, type Cell, recommend } from '../../apps/web/lib/blackjack/strategy';
import { coach } from '../../apps/web/lib/blackjack/coaching';
import { DRILLS, decisionFor, practiceBag, practicePool, scenario } from '../../apps/web/lib/blackjack/practice';
import { LESSONS } from '../../apps/web/lib/blackjack/lessons';
import { DEFAULT_PROGRESS, parseProgress } from '../../apps/web/lib/blackjack/progress';

// Independent oracle transcribed from the reference's written S17 rules, not from
// the application's row data. https://wizardofodds.com/games/blackjack/strategy/4-decks/
function hardReference(total: number, up: number): Cell {
  if (total === 15 && up === 10 || total === 16 && up >= 9) return 'R';
  if (total === 9 && up >= 3 && up <= 6 || total === 10 && up <= 9 || total === 11 && up <= 10) return 'D';
  if (total >= 17 || total === 12 && up >= 4 && up <= 6 || total >= 13 && total <= 16 && up <= 6) return 'S';
  return 'H';
}
function softReference(total: number, up: number): Cell {
  if (total >= 19) return 'S';
  if (total === 18) return up >= 3 && up <= 6 ? 'Ds' : up >= 9 ? 'H' : 'S';
  if (total === 17 && up >= 3 && up <= 6 || (total === 15 || total === 16) && up >= 4 && up <= 6 || (total === 13 || total === 14) && up >= 5 && up <= 6 || total === 12 && up === 6) return 'D';
  return 'H';
}
function pairReference(pair: number, up: number): Cell {
  if (pair === 11 || pair === 8) return 'P';
  if ((pair === 2 || pair === 3 || pair === 7) && up <= 7 || pair === 4 && (up === 5 || up === 6) || pair === 6 && up <= 6 || pair === 9 && (up <= 6 || up === 8 || up === 9)) return 'P';
  if (pair === 5) return up <= 9 ? 'D' : 'H';
  return pair >= 9 ? 'S' : 'H';
}
function expectedAction(cell: Cell, legal: readonly Action[]): Action {
  switch (cell) {
    case 'D': return legal.includes('double') ? 'double' : 'hit';
    case 'Ds': return legal.includes('double') ? 'double' : 'stand';
    case 'R': return legal.includes('surrender') ? 'surrender' : 'hit';
    case 'P': return 'split';
    case 'S': return 'stand';
    default: return 'hit';
  }
}

for (const total of Object.keys(HARD).map(Number)) test(`hard ${total}: all ten reference entries and double/surrender fallbacks`, () => {
  const cards: Rank[] = total <= 11 ? [String(Math.floor(total / 2)) as Rank, String(Math.ceil(total / 2)) as Rank] : total === 21 ? ['10', '9', '2'] : ['10', String(total - 10) as Rank];
  for (const [index, up] of UPCARDS.entries()) {
    const expected = hardReference(total, up);
    expect(HARD[total][index], `hard ${total} vs ${up}`).toBe(expected);
    for (const legal of [['hit', 'stand', 'double', 'surrender'], ['hit', 'stand', 'double'], ['hit', 'stand', 'surrender'], ['hit', 'stand']] as Action[][]) {
      expect(recommend({ cards: exampleCards(cards), dealerUpcard: exampleCards([up === 11 ? 'A' : String(up) as Rank])[0], legal })).toBe(total === 21 ? null : expectedAction(expected, legal));
    }
  }
});

for (const total of Object.keys(SOFT).map(Number)) test(`soft ${total}: all ten reference entries and doubling fallback`, () => {
  const cards: Rank[] = ['A', total === 12 ? 'A' : String(total - 11) as Rank];
  for (const [index, up] of UPCARDS.entries()) {
    const expected = softReference(total, up);
    expect(SOFT[total][index], `soft ${total} vs ${up}`).toBe(expected);
    for (const legal of [['hit', 'stand', 'double'], ['hit', 'stand']] as Action[][]) {
      expect(recommend({ cards: exampleCards(cards), dealerUpcard: exampleCards([up === 11 ? 'A' : String(up) as Rank])[0], legal })).toBe(total === 21 ? null : expectedAction(expected, legal));
    }
  }
});

for (const pair of Object.keys(PAIRS).map(Number)) test(`pair ${pair}: all ten reference entries and split-unavailable fallback`, () => {
  const rank = pair === 11 ? 'A' : String(pair) as Rank;
  for (const [index, up] of UPCARDS.entries()) {
    const item = scenario([rank, rank], up === 11 ? 'A' : String(up) as Rank);
    const expected = pairReference(pair, up);
    expect(PAIRS[pair][index], `pair ${pair} vs ${up}`).toBe(expected);
    expect(recommend(decisionFor(item))).toBe(expectedAction(expected, decisionFor(item).legal));
    for (const available of [['hit', 'stand', 'double', 'surrender'], ['hit', 'stand', 'double'], ['hit', 'stand', 'surrender'], ['hit', 'stand']] as Action[][]) {
      const total = score(item.hand.cards);
      const fallback = total.soft ? softReference(total.total, up) : hardReference(total.total, up);
      expect(recommend({ ...decisionFor(item), legal: available })).toBe(expectedAction(fallback, available));
    }
  }
});

for (const [ranks, dealer, expected] of [
  [['10', '2'], '6', 'stand'], [['10', '2'], '2', 'hit'], [['10', '6'], '10', 'surrender'],
  [['8', '8'], '10', 'split'], [['A', 'A'], 'A', 'split'], [['10', '10'], '6', 'stand'],
  [['9', '9'], '7', 'stand'], [['6', '5'], '6', 'double'], [['6', '5'], 'A', 'hit'],
  [['A', '7'], '9', 'hit'], [['A', '7'], '6', 'double'], [['4', '4'], '5', 'split'],
] as [Rank[], Rank, Action][]) test(`verified landmark: ${ranks.join('+')} vs ${dealer} → ${expected}`, () => {
  expect(recommend(decisionFor(scenario(ranks, dealer)))).toBe(expected);
});

test('unaffordable double/split and maximum-hand fallbacks use the actual legal actions', () => {
  expect(recommend(decisionFor(scenario(['6', '5'], '6', { bankroll: 500 })))).toBe('hit');
  expect(recommend(decisionFor(scenario(['A', '7'], '6', { bankroll: 500 })))).toBe('stand');
  expect(recommend(decisionFor(scenario(['8', '8'], '10', { bankroll: 500 })))).toBe('surrender');
  expect(recommend(decisionFor(scenario(['A', 'A'], '6', { bankroll: 500 })))).toBe('hit');
  const item = scenario(['8', '8'], '10', { fromSplit: true });
  const legal = legalHandActions(item.hand, item.bankroll, 4);
  expect(legal).not.toContain('split'); expect(recommend({ ...decisionFor(item), legal })).toBe('hit');
});

test('subsequent decisions and split hands cannot receive an illegal recommendation', () => {
  expect(recommend(decisionFor(scenario(['10', '2', '4'], '10')))).toBe('hit');
  expect(recommend(decisionFor(scenario(['A', '2', '5'], '6')))).toBe('stand');
  expect(recommend(decisionFor(scenario(['2', '3', '6'], '6')))).toBe('hit');
  expect(recommend(decisionFor(scenario(['10', '6'], '10', { fromSplit: true })))).toBe('hit');
  expect(recommend(decisionFor(scenario(['8', '3'], '6', { fromSplit: true })))).toBe('double');
});

test('coaching evaluates the pre-draw decision, names its context and honors affordability', () => {
  const decision = decisionFor(scenario(['6', '5'], '6'));
  const feedback = coach(decision, 'double');
  expect(feedback.correct).toBe(true); expect(feedback.context).toContain('Hard 11 vs 6'); expect(feedback.title).toContain('Double down');
  expect(coach(decisionFor(scenario(['6', '5'], '6', { bankroll: 0 })), 'hit').correct).toBe(true);
  expect(coach(decisionFor(scenario(['10', '2'], '6')), 'hit').title).toBe('Best move: Stand.');
});

test('practice has hundreds of distinct valid decisions and category counterexamples', () => {
  const all = practicePool('All Hands');
  expect(all.length).toBeGreaterThan(600); expect(new Set(all.map(item => item.id)).size).toBe(all.length);
  for (const drill of DRILLS) {
    const bag = practiceBag(drill, () => .3);
    expect(bag.length).toBeGreaterThanOrEqual(20);
    expect(new Set(bag.map(item => item.id)).size).toBe(bag.length);
    for (const item of bag) {
      const decision = decisionFor(item);
      expect(decision.legal).toContain(recommend(decision));
      expect(score(item.hand.cards).total).toBeLessThan(21);
    }
  }
  const doubling = new Set(practicePool('Doubling').map(item => recommend(decisionFor(item))));
  expect(doubling.has('double')).toBe(true); expect(doubling.has('hit')).toBe(true); expect(doubling.has('stand')).toBe(true);
  const surrender = new Set(practicePool('Surrender').map(item => recommend(decisionFor(item))));
  expect(surrender.has('surrender')).toBe(true); expect(surrender.has('hit')).toBe(true); expect(surrender.has('split')).toBe(true);
});

test('every lesson exercise has a real answer and every strategic lesson uses the evaluator', () => {
  expect(LESSONS).toHaveLength(7);
  for (const lesson of LESSONS) {
    expect(lesson.steps.length).toBeGreaterThanOrEqual(4);
    for (const step of lesson.steps) {
      const exercise = step.exercise;
      if (exercise.kind === 'decision') {
        const decision = decisionFor(scenario(exercise.cards, exercise.dealer, { fromSplit: exercise.fromSplit, bankroll: exercise.bankroll }));
        expect(decision.legal).toContain(recommend(decision));
      } else if (exercise.kind === 'value') {
        const value = score(exampleCards(exercise.cards));
        expect(exercise.choices).toContain(`${value.soft ? 'Soft' : 'Hard'} ${value.total}`);
      } else expect(exercise.choices[exercise.answer]).toBeTruthy();
    }
  }
});

test('saved progress validates untrusted local data and recovers from corruption', () => {
  for (const value of [null, '', 'not json', 'null', '[]']) expect(parseProgress(value)).toEqual(DEFAULT_PROGRESS);
  expect(parseProgress('{"completed":[0,0,6,-1,7,"2",1.5],"lesson":99,"coach":"no"}')).toEqual({ completed: [0, 6], lesson: 0, coach: true });
  expect(parseProgress('{"completed":[2],"lesson":5,"coach":false}')).toEqual({ completed: [2], lesson: 5, coach: false });
});
