import { test, expect, type Page } from '@playwright/test';
import { type Rank, createShoe, isBlackjack } from '../../apps/web/lib/blackjack/cards';
import { type Action, type Game, act, deal, dealerStep, insuranceDecision, newGame } from '../../apps/web/lib/blackjack/engine';
import { LESSONS } from '../../apps/web/lib/blackjack/lessons';
import { scenario, decisionFor } from '../../apps/web/lib/blackjack/practice';
import { ACTION_LABELS, recommend } from '../../apps/web/lib/blackjack/strategy';
import { LEGACY_PROGRESS_KEY } from '../../apps/web/lib/blackjack/progress';
import { handLabel, exampleCards } from '../../apps/web/lib/blackjack/cards';
import { money } from '../../apps/web/lib/blackjack/rules';

async function open(page: Page, mode = 'Learn') {
  await page.goto('/blackjack');
  await expect(page.getByRole('heading', { name: 'Blackjack Lab' })).toBeVisible();
  await page.getByRole('tab', { name: new RegExp(`^${mode}`) }).click();
  await expect(page.getByRole('tab', { name: new RegExp(`^${mode}`) })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('tabpanel')).toBeVisible();
}
async function fixedShuffle(page: Page, value = .999999) {
  // Set only AFTER hydration: React/Next need unique random internal instance keys.
  // This controls the upcoming shuffle without adding an application fixture hook.
  await page.evaluate(value => { Math.random = () => value; }, value);
}
function randomFor(predicate: (game: Game) => boolean) {
  for (let i = 1; i < 10000; i++) {
    const value = i / 10000;
    const game = deal(newGame(createShoe(() => value)), 1000);
    if (predicate(game)) return { value, game };
  }
  throw new Error('Could not find deterministic teaching deal');
}

async function pausedPlay(page: Page, random = .999999) {
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await page.clock.install({ time: new Date('2026-10-30T12:00:00Z') });
  await open(page, 'Play');
  await fixedShuffle(page, random);
  await page.clock.pauseAt(new Date('2026-10-30T13:00:00Z'));
  return page.getByRole('tabpanel');
}

async function finishTableMotion(page: Page) {
  const table = page.getByRole('tabpanel').getByLabel('Blackjack table', { exact: true });
  for (let step = 0; step < 40 && await table.getAttribute('aria-busy') === 'true'; step++) {
    await page.clock.runFor(200);
  }
  await expect(table).toHaveAttribute('aria-busy', 'false');
}

test('browser: two tabs preserve completed work when coaching changes and synchronize resets', async ({ page, context }) => {
  await open(page);
  const other = await context.newPage(); await open(other);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: /Hard Hands/ }).click();
  const lesson = LESSONS.find(lesson => lesson.id === 'hard-hands')!;
  for (let index = 0; index < lesson.steps.length; index++) {
    await panel.locator('.bj-exercise button').first().click();
    if (index + 1 < lesson.steps.length) await panel.getByRole('button', { name: 'Next', exact: true }).click();
  }
  await panel.getByRole('button', { name: 'Complete lesson', exact: true }).click();
  await other.getByRole('tab', { name: /^Play/ }).click();
  await other.getByRole('switch', { name: 'Coach', exact: true }).click();
  await page.reload();
  await expect(page.getByRole('tabpanel')).toContainText(`1/${LESSONS.length} complete`);
  await expect(page.getByRole('heading', { name: 'Hard Hands', exact: true })).toBeVisible();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(page.getByRole('switch', { name: 'Coach', exact: true })).toHaveAttribute('aria-checked', 'false');
  await page.getByRole('tab', { name: /^Learn/ }).click();
  await other.getByRole('button', { name: 'Reset lesson progress & preferences' }).click();
  await expect(page.getByRole('tabpanel')).toContainText(`0/${LESSONS.length} complete`);
  await expect(page.getByRole('heading', { name: 'Blackjack Fundamentals', exact: true })).toBeVisible();
  await page.reload(); await expect(page.getByRole('tabpanel')).toContainText(`0/${LESSONS.length} complete`);
  await other.close();
});

test('browser: old numeric progress migrates once to stable lesson IDs', async ({ page }) => {
  await page.addInitScript(key => {
    if (!localStorage.getItem('test-migration-seeded')) {
      localStorage.setItem(key, JSON.stringify({ completed: [0, 2], lesson: 2, coach: false }));
      localStorage.setItem('test-migration-seeded', 'true');
    }
  }, LEGACY_PROGRESS_KEY);
  await open(page);
  await expect(page.getByRole('tabpanel')).toContainText(`2/${LESSONS.length} complete`);
  await expect(page.getByRole('heading', { name: 'Hard Hands', exact: true })).toBeVisible();
  expect(await page.evaluate(key => localStorage.getItem(key), LEGACY_PROGRESS_KEY)).toBeNull();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(page.getByRole('switch', { name: 'Coach', exact: true })).toHaveAttribute('aria-checked', 'false');
  await page.getByRole('button', { name: 'Reset lesson progress & preferences' }).click();
  await page.reload(); await expect(page.getByRole('tabpanel')).toContainText(`0/${LESSONS.length} complete`);
});

for (const motion of ['no-preference', 'reduce'] as const) {
  test(`browser: keyboard focus follows deal, hit and settlement with ${motion} motion`, async ({ page }) => {
    const panel = await pausedPlay(page);
    await page.emulateMedia({ reducedMotion: motion });
    await panel.getByRole('button', { name: 'Deal · $10', exact: true }).focus();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeFocused();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.locator('.bj-player-hand').getByRole('img')).toHaveCount(3);
    await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(panel.getByRole('button', { name: 'Stand', exact: true })).toBeFocused();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.locator('.bj-round-result')).toBeVisible();
    await expect(panel.getByRole('button', { name: 'Deal · $10', exact: true })).toBeFocused();
  });
}

for (const motion of ['no-preference', 'reduce'] as const) {
  test(`browser: keyboard focus reaches insurance and split decisions with ${motion} motion`, async ({ page }) => {
    const insurance = randomFor(game => game.phase === 'insurance' && !isBlackjack(game.dealer) && !isBlackjack(game.hands[0].cards));
    const panel = await pausedPlay(page, insurance.value);
    await page.emulateMedia({ reducedMotion: motion });
    await panel.getByRole('button', { name: 'Deal · $10', exact: true }).focus();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.getByRole('button', { name: 'Decline insurance', exact: true })).toBeFocused();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeFocused();
    await panel.getByRole('button', { name: 'Stand', exact: true }).focus();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await panel.getByRole('button', { name: 'Reset table · $500' }).click();
    const split = randomFor(game => game.phase === 'player' && game.hands[0].cards.every(card => card.rank === '8'));
    await fixedShuffle(page, split.value);
    await panel.getByRole('button', { name: 'Deal · $10', exact: true }).focus();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await panel.getByRole('button', { name: 'Split', exact: true }).focus();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.locator('.bj-player-hand')).toHaveCount(2);
    await expect(panel.locator('.bj-actions button:focus')).toHaveCount(1);
    await panel.getByRole('button', { name: 'Stand', exact: true }).focus();
    await page.keyboard.press('Enter');
    const learn = page.getByRole('tab', { name: /^Learn/ }); await learn.click();
    await page.clock.runFor(10000);
    await expect(learn).toBeFocused();
  });
}

test('browser: motion deals in order, guards rapid actions and reveals the dealer before paying', async ({ page }) => {
  const panel = await pausedPlay(page);
  // Two clicks in one event turn exercise the synchronous guard, before React disables controls.
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).evaluate(button => { button.click(); button.click(); });
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await expect(panel.locator('.bj-player-hand').getByRole('img')).toHaveCount(1);
  await expect(panel.locator('.bj-dealer').getByRole('img')).toHaveCount(0);
  await expect(panel.locator('.bj-player-hand .bj-total')).toHaveText('Hard 2');
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeDisabled();
  await page.clock.runFor(170);
  await expect(panel.locator('.bj-dealer').getByRole('img', { name: '3 of clubs' })).toBeVisible();
  await expect(panel.locator('.bj-player-hand').getByRole('img')).toHaveCount(1);
  await page.clock.runFor(170);
  await expect(panel.locator('.bj-player-hand').getByRole('img')).toHaveCount(2);
  await expect(panel.locator('.bj-dealer').getByRole('img')).toHaveCount(1);
  await page.clock.runFor(170);
  await expect(panel.locator('.bj-dealer').getByRole('img', { name: 'Face-down card' })).toBeVisible();
  await expect(panel.getByRole('img', { name: '5 of clubs' })).toHaveCount(0);
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeDisabled();
  await finishTableMotion(page);
  await panel.getByRole('button', { name: 'Hit', exact: true }).evaluate(button => { button.click(); button.click(); });
  await expect(panel.locator('.bj-player-hand').getByRole('img')).toHaveCount(3);
  await expect(panel.locator('.bj-player-hand .bj-total')).toHaveText('Hard 12');
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeDisabled();
  await finishTableMotion(page);
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(panel.getByRole('img', { name: '5 of clubs' })).toHaveCount(0);
  await page.clock.runFor(220);
  await expect(panel.getByRole('img', { name: '5 of clubs' })).toBeVisible();
  await expect(panel.locator('.bj-table-status')).toContainText('reveals the hole card');
  await expect(panel.locator('.bj-round-result')).toHaveCount(0);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await finishTableMotion(page);
  await expect(panel.locator('.bj-round-result')).toContainText('+$10');
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$510');
});

test('browser: motion holds natural payouts until the deal and reveal finish', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'settled' && game.hands[0].outcome === 'blackjack');
  const panel = await pausedPlay(page, fixed.value);
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await expect(panel.locator('.bj-round-result')).toHaveCount(0);
  await expect(panel.getByRole('button', { name: /^Deal/ })).toHaveCount(0);
  await expect(panel.locator('.bj-player-hand')).not.toContainText('Blackjack');
  await finishTableMotion(page);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$515');
  await expect(panel.locator('.bj-round-result')).toContainText('+$15');
  await expect(panel.locator('.bj-player-hand')).toContainText('Blackjack · 21');
  // A following round has no leftover result or payout timer.
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$505');
  await expect(panel.locator('.bj-round-result')).toHaveCount(0);
});

test('browser: motion keeps split Ace cards separate and survives mode switches', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'player' && game.hands[0].cards.every(card => card.rank === 'A'));
  const panel = await pausedPlay(page, fixed.value);
  await page.setViewportSize({ width: 320, height: 844 });
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await finishTableMotion(page);
  await panel.getByRole('button', { name: 'Split', exact: true }).click();
  await expect(panel.locator('.bj-player-hand')).toHaveCount(2);
  await expect(panel.locator('.bj-player-hand').nth(0).getByRole('img')).toHaveCount(1);
  await expect(panel.locator('.bj-player-hand').nth(1).getByRole('img')).toHaveCount(1);
  await page.clock.runFor(220);
  await expect(panel.locator('.bj-player-hand').nth(0).getByRole('img')).toHaveCount(2);
  await expect(panel.locator('.bj-player-hand').nth(1).getByRole('img')).toHaveCount(1);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$480');
  await page.getByRole('tab', { name: /^Learn/ }).click();
  await page.clock.runFor(400);
  await page.getByRole('tab', { name: /^Play/ }).click();
  await finishTableMotion(page);
  let expected = act(fixed.game, 'split');
  while (expected.phase === 'dealer') expected = dealerStep(expected);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText(money(expected.bankroll));
  await expect(panel.locator('.bj-round-result')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('browser: reduced motion skips current and future animation delays', async ({ page }) => {
  const panel = await pausedPlay(page);
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeDisabled();
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeEnabled();
  await expect(panel.locator('.bj-player-hand').getByRole('img')).toHaveCount(2);
  await panel.getByRole('button', { name: 'Hit', exact: true }).click();
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeEnabled();
  await expect(panel.locator('.bj-player-hand .bj-total')).toHaveText('Hard 12');
  const style = await panel.locator('.bj-player-hand .bj-card').first().evaluate(card => ({
    animation: getComputedStyle(card).animationName,
    transition: getComputedStyle(card.querySelector('.bj-card-inner')!).transitionDuration,
  }));
  expect(style).toEqual({ animation: 'none', transition: '0s' });
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await finishTableMotion(page);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$510');
  await panel.getByRole('button', { name: 'Reset table · $500' }).click();
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeEnabled();
  await expect(panel.getByLabel('Blackjack table', { exact: true })).toHaveAttribute('aria-busy', 'false');
});

test('browser: public navigation, all seven lessons, completion, persistence, reset and keyboard modes', async ({ page }) => {
  const apiRequests: string[] = [];
  page.on('request', request => { if (request.url().includes('/api/')) apiRequests.push(request.url()); });
  await open(page);
  await expect(page.getByRole('navigation', { name: 'Public navigation', exact: true }).getByRole('link', { name: 'Blackjack Lab' })).toHaveAttribute('href', '/blackjack');
  const panel = page.getByRole('tabpanel');
  await expect(panel).toContainText('Atlantic City rules: 8 decks');
  await expect(page.locator('meta[name="description"]')).toHaveAttribute('content', /Atlantic City.*8-deck S17/);
  const rules = page.locator('.bj-rules');
  await rules.locator('summary').click();
  await expect(rules).toContainText('8 standard 52-card decks');
  await expect(rules).toContainText('three times for four hands total');
  await expect(rules).toContainText('Casino and online tables can vary');
  await rules.locator('summary').click();
  await page.screenshot({ path: 'test-results/blackjack-learn-desktop.png', fullPage: true });
  for (const [lessonIndex, lesson] of LESSONS.entries()) {
    await panel.getByRole('button', { name: new RegExp(lesson.title) }).click();
    await expect(panel.getByRole('heading', { name: lesson.title, exact: true })).toBeVisible();
    for (const [stepIndex, step] of lesson.steps.entries()) {
      await expect(panel.getByRole('heading', { name: step.title, exact: true })).toBeVisible();
      if (step.demo) {
        await panel.getByRole('button', { name: /^Try / }).click();
        await expect(panel.getByRole('button', { name: 'Replay example' })).toBeVisible();
      }
      const exercise = step.exercise;
      const answer = exercise.kind === 'quiz' ? exercise.choices[exercise.answer]
        : exercise.kind === 'value' ? handLabel(exampleCards(exercise.cards))
        : ACTION_LABELS[recommend(decisionFor(scenario(exercise.cards, exercise.dealer, { fromSplit: exercise.fromSplit, bankroll: exercise.bankroll })))!];
      await panel.locator('.bj-exercise').getByRole('button', { name: answer, exact: true }).click();
      await expect(panel.locator('.bj-feedback')).toContainText('Correct');
      if (stepIndex < lesson.steps.length - 1) await panel.getByRole('button', { name: 'Next', exact: true }).click();
    }
    await panel.getByRole('button', { name: 'Complete lesson', exact: true }).click();
    await expect(panel).toContainText(`${lessonIndex + 1}/7 complete`);
  }
  await page.reload();
  await expect(page.getByRole('tabpanel')).toContainText('7/7 complete');
  await expect(page.getByRole('heading', { name: 'Putting It Together' })).toBeVisible();
  await page.getByRole('tab', { name: /^Learn/ }).focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: /^Practice/ })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'One hand. One decision.' })).toBeVisible();
  await page.getByRole('button', { name: 'Reset lesson progress & preferences' }).click();
  await page.reload();
  await expect(page.getByRole('tabpanel')).toContainText('0/7 complete');
  await expect(page.getByRole('heading', { name: 'Blackjack Fundamentals' })).toBeVisible();
  expect(apiRequests).toEqual([]);
});

test('browser: practice gives accurate feedback, counts once, varies hands and restarts categories', async ({ page }) => {
  await open(page, 'Practice');
  const panel = page.getByRole('tabpanel');
  await panel.locator('.bj-reference summary').click();
  await expect(panel.locator('.bj-chart-content')).toContainText('Atlantic City · 8 decks · S17 · 3:2 blackjack');
  await panel.locator('.bj-reference summary').click();
  await panel.getByLabel('Drill category').selectOption('Surrender');
  const seen = new Set<string>();
  for (let index = 0; index < 12; index++) {
    const groups = panel.locator('.bj-drill-table .bj-hand-cards');
    const dealer = (await groups.nth(0).getByRole('img').getAttribute('aria-label'))!.split(' of ')[0] as Rank;
    const cards = await groups.nth(1).getByRole('img').evaluateAll(images => images.map(image => image.getAttribute('aria-label')!.split(' of ')[0])) as Rank[];
    const key = `${cards.join('+')}:${dealer}`;
    expect(seen.has(key)).toBe(false); seen.add(key);
    const decision = decisionFor(scenario(cards, dealer));
    const best = recommend(decision)!;
    const chosen = index === 0 ? decision.legal.find(action => action !== best)! : best;
    await panel.getByRole('button', { name: ACTION_LABELS[chosen], exact: true }).click();
    await expect(panel.locator('.bj-feedback strong')).toHaveText(`${index === 0 ? 'Best move:' : 'Correct —'} ${ACTION_LABELS[best]}.`);
    await expect(panel.getByLabel('Practice score')).toContainText(`${Math.max(0, index)} / ${index + 1}`);
    await expect(panel.getByRole('button', { name: ACTION_LABELS[chosen], exact: true })).toBeDisabled();
    await panel.getByRole('button', { name: 'Next hand' }).click();
  }
  await panel.getByRole('button', { name: 'Restart session' }).click();
  await expect(panel.getByLabel('Practice score')).toContainText('0 / 0');
  for (const category of ['Hard Totals', 'Soft Totals', 'Splitting', 'Doubling', 'All Hands']) {
    await panel.getByLabel('Drill category').selectOption(category);
    await expect(panel.locator('.bj-drill-table').getByRole('img').first()).toBeVisible();
  }
});

test('browser: full playable hand, hidden hole card, pre-action coaching and bankroll', async ({ page }) => {
  await open(page, 'Play');
  await fixedShuffle(page);
  const panel = page.getByRole('tabpanel');
  await expect(panel.getByRole('switch', { name: 'Coach' })).toBeChecked();
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await expect(panel.locator('.bj-dealer').getByRole('img', { name: 'Face-down card' })).toBeVisible();
  await expect(panel.getByRole('img', { name: '5 of clubs' })).toHaveCount(0);
  await panel.getByRole('button', { name: 'Hit', exact: true }).click();
  await expect(panel.locator('.bj-feedback')).toContainText('Hard 6 vs 3');
  await expect(panel.locator('.bj-player-hand')).toContainText('Hard 12');
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(panel.locator('.bj-feedback')).toContainText('Best move: Hit.');
  await expect(panel.locator('.bj-feedback')).toContainText('Hard 12 vs 3');
  await expect(panel.locator('.bj-round-result')).toContainText('+$10');
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$510');
  await expect(panel.locator('.bj-player-hand .bj-card')).toHaveCount(3);
  await expect(panel.getByRole('img', { name: '5 of clubs' })).toBeVisible();
  await page.screenshot({ path: 'test-results/blackjack-play-desktop.png', fullPage: true });
  await panel.getByRole('switch', { name: 'Coach' }).click();
  await expect(panel.locator('.bj-feedback')).toHaveCount(0);
  await page.reload(); await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(panel.getByRole('switch', { name: 'Coach' })).not.toBeChecked();
  // Reload restores native randomness; fix the new page's shuffle after hydration.
  await fixedShuffle(page);
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(panel.locator('.bj-round-result')).toBeVisible();
  await expect(panel.locator('.bj-feedback')).toHaveCount(0);
  await panel.getByRole('button', { name: 'Reset table · $500' }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$500');
});

test('browser: split hands advance visibly and settle the expected balance on mobile', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'player' && game.hands[0].cards.every(card => card.rank === '8'));
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, 'Play');
  await fixedShuffle(page, fixed.value);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await panel.getByRole('button', { name: 'Split', exact: true }).click();
  await expect(panel.getByRole('region', { name: 'Player hand 1, active', exact: true })).toBeVisible();
  await expect(panel.getByRole('region', { name: 'Player hand 2', exact: true })).toContainText('awaiting second card');
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$480');
  await page.screenshot({ path: 'test-results/blackjack-split-mobile.png', fullPage: true });
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(panel.getByRole('region', { name: 'Player hand 2, active', exact: true })).toBeVisible();
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  let expected = act(fixed.game, 'split');
  while (expected.phase === 'player') expected = act(expected, 'stand');
  while (expected.phase === 'dealer') expected = dealerStep(expected);
  await expect(panel.locator('.bj-round-result')).toBeVisible();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText(money(expected.bankroll));
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('browser: insurance comes before peek and is settled independently', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'insurance' && ['10', 'J', 'Q', 'K'].includes(game.dealer[1].rank) && game.hands[0].cards.every(card => card.rank !== 'A'));
  await open(page, 'Play'); await fixedShuffle(page, fixed.value);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await expect(panel.getByRole('heading', { name: 'Insurance?', exact: true })).toBeVisible();
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toHaveCount(0);
  await expect(panel.locator('.bj-dealer .bj-total')).toHaveText('A showing');
  await panel.getByRole('button', { name: 'Take insurance · $5' }).click();
  await expect(panel.locator('.bj-round-result')).toContainText('Even');
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$500');
  await expect(panel.locator('.bj-feedback')).toContainText('Best move: Decline insurance.');
  await expect(panel.locator('.bj-dealer .bj-total')).toHaveText('Blackjack · 21');
  await expect(panel.locator('.bj-table-status')).toContainText('The round ends immediately');
  await expect(panel.getByRole('button', { name: /^(Hit|Stand)$/ })).toHaveCount(0);
  await expect(panel.locator('.bj-player-hand')).toContainText('Hand return $0 · Hand net −$10');
  const breakdown = panel.getByRole('table', { name: 'Round wager breakdown' });
  const row = (name: string | RegExp) => breakdown.getByRole('row').filter({ has: page.getByRole('rowheader', { name, exact: true }) });
  await expect(row('Main bet').getByRole('cell')).toHaveText(['$10', '$0', '−$10']);
  await expect(row(/^Insurance/).getByRole('cell')).toHaveText(['$5', '$15', '+$10']);
  await expect(row('Total').getByRole('cell')).toHaveText(['$15', '$15', '$0']);
  await page.screenshot({ path: 'test-results/blackjack-insurance-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 320, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await panel.locator('.bj-round-result').screenshot({ path: 'test-results/blackjack-insurance-mobile.png' });
  // Declining changes only the side bet; the same dealer blackjack still ends play.
  await panel.getByRole('button', { name: 'Reset table · $500' }).click();
  await fixedShuffle(page, fixed.value);
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await panel.getByRole('button', { name: 'Decline insurance', exact: true }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await expect(panel.getByRole('button', { name: /^(Hit|Stand)$/ })).toHaveCount(0);
  await expect(row(/^Insurance/)).toHaveCount(0);
  await expect(row('Total').getByRole('cell')).toHaveText(['$10', '$0', '−$10']);
});

test('browser: losing insurance leaves the main hand playable and appears in the final total', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'insurance' && !isBlackjack(game.dealer) && !isBlackjack(game.hands[0].cards));
  await open(page, 'Play'); await fixedShuffle(page, fixed.value);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await panel.getByRole('button', { name: 'Take insurance · $5' }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$485');
  await expect(panel.locator('.bj-insurance-result')).toHaveText('Insurance lost: $5. Your main bet is still in play.');
  await expect(panel.locator('.bj-dealer').getByRole('img', { name: 'Face-down card' })).toBeVisible();
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeEnabled();
  await expect(panel.locator('.bj-round-result')).toHaveCount(0);
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  let expected = act(insuranceDecision(fixed.game, true), 'stand');
  while (expected.phase === 'dealer') expected = dealerStep(expected);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText(money(expected.bankroll));
  const breakdown = panel.getByRole('table', { name: 'Round wager breakdown' });
  await expect(breakdown.getByRole('row').filter({ has: page.getByRole('rowheader', { name: /^Insurance/ }) }).getByRole('cell')).toHaveText(['$5', '$0', '−$5']);
  const total = breakdown.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Total', exact: true }) });
  await expect(total.getByRole('cell').nth(0)).toHaveText('$15');
  await expect(total.getByRole('cell').nth(1)).toHaveText(money(expected.hands[0].returned!));
  await expect(panel.locator('.bj-insurance-result')).toHaveCount(0);
});

test('browser: bad wagers are blocked and locally saved corruption is recoverable', async ({ page }) => {
  await page.addInitScript(key => localStorage.setItem(key, 'invalid JSON'), LEGACY_PROGRESS_KEY);
  await open(page, 'Play');
  const panel = page.getByRole('tabpanel');
  for (const amount of ['-1', '0', '4', '5.50', '501', '10000']) {
    await panel.getByLabel('Wager ($)', { exact: true }).fill(amount);
    await expect(panel.getByRole('button', { name: 'Deal', exact: true })).toBeDisabled();
    await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$500');
  }
  await panel.getByRole('button', { name: 'Bet $5', exact: true }).click();
  await expect(panel.getByRole('button', { name: 'Deal · $5', exact: true })).toBeEnabled();
});

test('browser: reference tables, narrow screens, light/dark and no-network play', async ({ page, context }) => {
  await open(page, 'Practice'); await fixedShuffle(page);
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  const panel = page.getByRole('tabpanel');
  await panel.locator('.bj-reference>summary').click();
  await expect(panel.getByRole('table', { name: 'Hard totals', exact: true })).toBeVisible();
  const row12 = panel.getByRole('table', { name: 'Hard totals', exact: true }).getByRole('row').filter({ has: page.getByRole('rowheader', { name: '12', exact: true }) });
  await expect(row12.getByRole('cell').nth(0)).toHaveText('H');
  await expect(row12.getByRole('cell').nth(4)).toHaveText('S');
  for (const width of [1440, 768, 390, 320]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    if (width === 390) await page.screenshot({ path: 'test-results/blackjack-chart-mobile.png', fullPage: true });
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.evaluate(() => { document.documentElement.dataset.theme = 'dark'; });
  await page.getByRole('tab', { name: /^Play/ }).click();
  await page.screenshot({ path: 'test-results/blackjack-play-dark-mobile.png', fullPage: true });
  await context.setOffline(true);
  await panel.getByRole('button', { name: 'Deal · $10', exact: true }).click();
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(panel.locator('.bj-round-result')).toBeVisible();
  await context.setOffline(false);
  expect(errors).toEqual([]);
});

test('browser: unavailable browser storage does not block lessons, drills or play', async ({ page }) => {
  await page.addInitScript(() => {
    Object.defineProperty(window, 'localStorage', { get() { throw new Error('Storage denied for test'); } });
  });
  await open(page);
  await expect(page.getByText('Browser storage is unavailable. Progress will last for this visit only.')).toBeVisible();
  await page.getByRole('tabpanel').getByRole('button', { name: 'Hard 16', exact: true }).click();
  await expect(page.locator('.bj-feedback').first()).toContainText('Correct');
  await page.getByRole('tab', { name: /^Practice/ }).click();
  await expect(page.getByRole('tabpanel').getByRole('button', { name: 'Hit', exact: true })).toBeEnabled();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(page.getByRole('tabpanel').getByRole('button', { name: 'Deal · $10' })).toBeEnabled();
});
