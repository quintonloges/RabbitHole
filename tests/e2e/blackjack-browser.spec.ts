import { test, expect, type Locator, type Page } from '@playwright/test';
import { type Rank, createShoe, isBlackjack } from '../../apps/web/lib/blackjack/cards';
import { type Action, type Game, act, deal, dealerStep, insuranceDecision, newGame } from '../../apps/web/lib/blackjack/engine';
import { LESSONS } from '../../apps/web/lib/blackjack/lessons';
import { scenario, decisionFor } from '../../apps/web/lib/blackjack/practice';
import { ACTION_LABELS, recommend } from '../../apps/web/lib/blackjack/strategy';
import { LEGACY_PROGRESS_KEY } from '../../apps/web/lib/blackjack/progress';
import { handLabel, exampleCards } from '../../apps/web/lib/blackjack/cards';
import { money } from '../../apps/web/lib/blackjack/rules';

async function open(page: Page, mode = 'Learn', openBetting = true) {
  await page.goto('/blackjack');
  await expect(page.getByRole('heading', { name: 'Blackjack Lab' })).toBeVisible();
  await page.getByRole('tab', { name: new RegExp(`^${mode}`) }).click();
  await expect(page.getByRole('tab', { name: new RegExp(`^${mode}`) })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('tabpanel')).toBeVisible();
  if (mode === 'Play' && openBetting) await page.getByRole('button', { name: 'Place your wager', exact: true }).click();
}
async function fixedShuffle(page: Page, value = .999999) {
  // Set only AFTER hydration: React/Next need unique random internal instance keys.
  // This controls the upcoming shuffle without adding an application fixture hook.
  await page.evaluate(value => { Math.random = () => value; }, value);
}
async function resetTable(panel: Locator) {
  const reset = panel.getByRole('button', { name: 'Reset table · $500', exact: true });
  if (!await reset.isVisible()) await panel.getByRole('button', { name: /^Change bet,/ }).click();
  await reset.click();
}
function randomFor(predicate: (game: Game) => boolean) {
  for (let i = 1; i < 10000; i++) {
    const value = i / 10000;
    const game = deal(newGame(createShoe(() => value)), 1000);
    if (predicate(game)) return { value, game };
  }
  throw new Error('Could not find deterministic teaching deal');
}

async function fullyInViewport(locator: Locator) {
  await expect(locator).toBeVisible();
  expect(await locator.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return bounds.top >= 0 && bounds.left >= 0 && bounds.bottom <= innerHeight && bounds.right <= innerWidth;
  })).toBe(true);
}

test('browser: mobile play preserves the original header, full-size cards and compact action row', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page);
  const hero = page.locator('.bj-hero');
  const before = await hero.boundingBox();
  const scroll = await page.evaluate(() => scrollY);
  await page.getByRole('tab', { name: /^Play/ }).click();
  expect(await hero.boundingBox()).toEqual(before);
  expect(await page.evaluate(() => scrollY)).toBe(scroll);
  const panel = page.getByRole('tabpanel');
  await expect(panel.getByRole('heading', { name: 'Take a seat.' })).toBeVisible();
  await expect(panel.locator('.bj-bank-bar')).toContainText('Available chips');
  await expect(panel.locator('.bj-bank-bar')).toContainText('On the table');
  await expect(panel.locator('.bj-bank-bar')).toContainText('Table limits');
  await expect(panel.locator('.bj-bank-bar>div')).toHaveCount(3);
  for (const column of await panel.locator('.bj-bank-bar>div').all()) await expect(column).toBeVisible();
  const fixed = randomFor(game => game.phase === 'player' && game.hands[0].cards.every(card => card.rank === '8'));
  await fixedShuffle(page, fixed.value);
  await panel.getByRole('button', { name: 'Place your wager', exact: true }).click();
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  const cards = panel.locator('.bj-player-hand .bj-card');
  expect(await cards.first().boundingBox()).toMatchObject({ width: 69, height: 97 });
  const buttons = panel.locator('.bj-decision-area>.bj-actions button');
  await expect(buttons).toHaveCount(5);
  const tops = await buttons.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().top));
  expect(new Set(tops).size).toBe(1);
  await panel.locator('.bj-play-table').evaluate(element => element.scrollIntoView({ block: 'start', behavior: 'instant' }));
  await page.screenshot({ path: 'test-results/blackjack-restored-mobile.png' });
});

test('browser: mobile coaching stays beside the actions through hits, dealer play and settlement', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const panel = await pausedPlay(page);
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await finishTableMotion(page);
  await panel.locator('.bj-play-table').evaluate(element => element.scrollIntoView({ block: 'start', behavior: 'instant' }));
  await panel.getByRole('button', { name: 'Hit', exact: true }).click();
  const feedback = panel.locator('.bj-feedback');
  await expect(feedback).toContainText('Hard 6 vs 3');
  await fullyInViewport(feedback);
  await finishTableMotion(page);
  await fullyInViewport(feedback);
  const actions = await panel.locator('.bj-decision-area>.bj-actions').boundingBox();
  const comments = await feedback.boundingBox();
  expect(comments!.y - (actions!.y + actions!.height)).toBeLessThan(24);
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(feedback).toContainText('Hard 12 vs 3');
  await fullyInViewport(feedback);
  await finishTableMotion(page);
  await expect(panel.getByRole('dialog', { name: 'Your next wager' })).toBeVisible();
  await panel.getByRole('button', { name: 'Close betting' }).click();
  await fullyInViewport(feedback);
  await page.screenshot({ path: 'test-results/blackjack-restored-coaching-mobile.png' });
});

test('browser: the betting drawer starts closed with its opener visible without scrolling', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, 'Play', false);
  const drawer = page.getByRole('dialog', { name: 'Place your wager', exact: true });
  const opener = page.getByRole('button', { name: 'Place your wager', exact: true });
  await expect(drawer).not.toBeVisible();
  await expect(opener).toHaveAttribute('aria-expanded', 'false');
  await fullyInViewport(opener);
  await expect(page.getByRole('tab', { name: /^Play/ })).toBeFocused();
  await expect(page.getByTestId('blackjack-bankroll')).toHaveText('$500');
  const scroll = await page.evaluate(() => scrollY);
  await page.screenshot({ path: 'test-results/blackjack-drawer-initial-mobile.png' });
  await opener.click();
  await expect(drawer).toBeVisible();
  await expect(opener).not.toBeVisible();
  expect(await page.evaluate(() => scrollY)).toBe(scroll);
  await drawer.getByRole('button', { name: 'Close betting' }).click();
  await page.getByRole('tab', { name: /^Learn/ }).click();
  await expect(opener).not.toBeVisible();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(drawer).not.toBeVisible();
  await fullyInViewport(opener);
});

for (const viewport of [{ width: 390, height: 844 }, { width: 375, height: 667 }, { width: 320, height: 568 }, { width: 844, height: 390 }]) {
  test(`browser: betting and re-betting drawers stay at the viewport bottom at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await open(page, 'Play');
    await fixedShuffle(page);
    const panel = page.getByRole('tabpanel');
    const dialog = panel.getByRole('dialog', { name: 'Place your wager', exact: true });
    await fullyInViewport(dialog);
    const bounds = await dialog.boundingBox();
    expect(bounds!.y + bounds!.height).toBe(viewport.height);
    expect(await dialog.evaluate(element => element.matches(':modal'))).toBe(false);
    for (const control of await dialog.locator('button, input').all()) await fullyInViewport(control);
    await expect(dialog.getByRole('button', { name: 'Bet $25', exact: true })).not.toBeVisible();
    await dialog.getByRole('button', { name: /^Change bet,/ }).click();
    await dialog.getByRole('button', { name: 'Bet $25', exact: true }).click();
    await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$500');
    await expect(dialog.getByLabel('Wager ($)')).not.toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Change bet, $25', exact: true })).toBeFocused();
    await dialog.getByRole('button', { name: 'Deal hand', exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeFocused();
    await panel.getByRole('button', { name: 'Stand', exact: true }).scrollIntoViewIfNeeded();
    await panel.getByRole('button', { name: 'Stand', exact: true }).click();
    const next = panel.getByRole('dialog', { name: 'Your next wager', exact: true });
    await fullyInViewport(next);
    const nextBounds = await next.boundingBox();
    expect(nextBounds!.y + nextBounds!.height).toBe(viewport.height);
    await expect(next.getByLabel('Wager ($)')).not.toBeVisible();
    await expect(next.getByRole('button', { name: /^Change bet,/ })).toHaveAttribute('aria-expanded', 'false');
    await expect(next.getByRole('button', { name: 'Deal hand', exact: true })).toBeFocused();
    for (const control of await next.locator('button, input').all()) await fullyInViewport(control);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    // The existing table reflows on settlement; opening and closing the drawer
    // itself must preserve the reader's position in that completed hand.
    const scroll = await page.evaluate(() => scrollY);
    await page.keyboard.press('Escape');
    await expect(panel.getByRole('button', { name: 'Bet again', exact: true })).toBeFocused();
    expect(await page.evaluate(() => scrollY)).toBe(scroll);
    await page.keyboard.press('Enter');
    await fullyInViewport(next);
    expect(await page.evaluate(() => scrollY)).toBe(scroll);
    await page.screenshot({ path: `test-results/blackjack-rebet-${viewport.width}x${viewport.height}.png` });
    const dealBounds = await next.getByRole('button', { name: 'Deal hand', exact: true }).boundingBox();
    await next.getByRole('button', { name: /^Round result:/ }).click();
    await expect(next.getByRole('table', { name: 'Round wager breakdown' })).toBeVisible();
    expect(await next.getByRole('button', { name: 'Deal hand', exact: true }).boundingBox()).toEqual(dealBounds);
    await next.getByRole('button', { name: /^Change bet,/ }).click();
    await expect(next.getByRole('table', { name: 'Round wager breakdown' })).not.toBeVisible();
    await expect(next.getByRole('button', { name: 'Bet $25', exact: true })).toBeFocused();
    expect(await next.getByRole('button', { name: 'Deal hand', exact: true }).boundingBox()).toEqual(dealBounds);
    await fullyInViewport(next.getByRole('button', { name: 'Close betting' }));
    await fullyInViewport(next.getByRole('button', { name: 'Deal hand', exact: true }));
    await expect(next.getByLabel('Wager ($)')).toHaveValue('25');
    await next.getByRole('button', { name: 'Bet $50', exact: true }).click();
    await next.getByRole('button', { name: 'Deal hand', exact: true }).click();
    await expect(next).not.toBeVisible();
    await expect(panel.locator('.bj-hand-bet')).toHaveText('$50');
  });
}

test('browser: the betting drawer focuses its editor and can be dismissed without placing a wager', async ({ page }) => {
  await open(page, 'Play');
  const panel = page.getByRole('tabpanel');
  const dialog = panel.getByRole('dialog', { name: 'Place your wager', exact: true });
  await dialog.getByRole('button', { name: /^Change bet,/ }).click();
  await expect(dialog.getByRole('button', { name: 'Bet $10', exact: true })).toBeFocused();
  await dialog.getByRole('button', { name: 'Bet $50', exact: true }).click();
  await expect(dialog.getByRole('button', { name: 'Change bet, $50', exact: true })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'Deal hand', exact: true })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).not.toBeVisible();
  const reopen = panel.getByRole('button', { name: 'Place your wager', exact: true });
  await expect(reopen).toBeFocused();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$500');
  await reopen.click();
  await expect(dialog.getByLabel('Wager ($)')).not.toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Deal hand', exact: true })).toBeFocused();
  await dialog.getByRole('button', { name: 'Close betting' }).click();
  await expect(reopen).toBeFocused();
  await page.getByRole('tab', { name: /^Learn/ }).click();
  await expect(dialog).not.toBeVisible();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(dialog).not.toBeVisible();
  await reopen.click();
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /^Change bet,/ }).click();
  await expect(dialog.getByLabel('Wager ($)')).toHaveValue('50');
  await resetTable(panel);
  await expect(dialog.getByLabel('Wager ($)')).not.toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Change bet, $10', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Deal hand', exact: true })).toBeFocused();
});

test('browser: the nonmodal drawer leaves the table scrollable and navigation usable', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, 'Play');
  const drawer = page.getByRole('dialog', { name: 'Place your wager', exact: true });
  expect(await drawer.evaluate(element => element.matches(':modal'))).toBe(false);
  expect(await page.evaluate(() => getComputedStyle(document.body).overflowY)).not.toBe('hidden');
  const before = await page.evaluate(() => scrollY);
  await page.mouse.move(100, 400); await page.mouse.wheel(0, 300);
  await expect.poll(() => page.evaluate(() => scrollY)).toBeGreaterThan(before);
  await expect(drawer).toBeVisible();
  await page.getByRole('switch', { name: 'Coach', exact: true }).focus();
  await expect(page.getByRole('switch', { name: 'Coach', exact: true })).toBeFocused();
  await page.getByRole('tab', { name: /^Learn/ }).click();
  await expect(drawer).not.toBeVisible();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(drawer).toBeVisible();
  await expect(page.getByRole('tab', { name: /^Play/ })).toBeFocused();
});

test('browser: settlement preserves deliberately moved focus while offering the next wager', async ({ page }) => {
  const panel = await pausedPlay(page);
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await finishTableMotion(page);
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  const coach = panel.getByRole('switch', { name: 'Coach', exact: true });
  await coach.focus();
  await finishTableMotion(page);
  await expect(panel.getByRole('dialog', { name: 'Your next wager' })).toBeVisible();
  await expect(coach).toBeFocused();
});

test('browser: the wager editor scrolls independently while Deal and Close remain reachable in a short viewport', async ({ page }) => {
  await page.setViewportSize({ width: 844, height: 390 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, 'Play');
  const drawer = page.getByRole('dialog', { name: 'Place your wager', exact: true });
  await drawer.getByRole('button', { name: /^Change bet,/ }).click();
  await page.setViewportSize({ width: 844, height: 260 });
  await fullyInViewport(drawer.getByRole('button', { name: 'Deal hand', exact: true }));
  await fullyInViewport(drawer.getByRole('button', { name: 'Close betting' }));
  expect(await drawer.locator('.bj-drawer-content').evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);
  await drawer.getByLabel('Wager ($)').fill('37');
  await drawer.getByRole('button', { name: 'Done', exact: true }).click();
  await expect(drawer.getByRole('button', { name: 'Change bet, $37', exact: true })).toBeFocused();
  await expect(drawer.getByRole('button', { name: 'Deal hand', exact: true })).toBeEnabled();
});

test('browser: the drawer shows an expandable result and repeats the wager without opening bet options', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, 'Play'); await fixedShuffle(page);
  const panel = page.getByRole('tabpanel');
  await expect(panel.locator('.bj-chip-row')).not.toBeVisible();
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await panel.getByRole('button', { name: 'Hit', exact: true }).click();
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  const drawer = panel.getByRole('dialog', { name: 'Your next wager' });
  const summary = drawer.getByRole('button', { name: /^Round result:/ });
  const table = drawer.getByRole('table', { name: 'Round wager breakdown' });
  await expect(summary).toHaveText('Last round+$10');
  await expect(table).not.toBeVisible();
  await expect(panel.locator('.bj-round-summary')).toHaveCount(1);
  await expect(drawer.locator('.bj-chip-row')).not.toBeVisible();
  const collapsed = await drawer.boundingBox();
  await summary.focus(); await page.keyboard.press('Enter');
  await expect(table).toBeVisible();
  await expect(table.getByRole('row').filter({ has: page.getByRole('rowheader', { name: 'Total', exact: true }) }).getByRole('cell')).toHaveText(['$10', '$20', '+$10']);
  expect((await drawer.boundingBox())!.height).toBeGreaterThan(collapsed!.height);
  await fullyInViewport(drawer.getByRole('button', { name: 'Deal hand', exact: true }));
  await page.screenshot({ path: 'test-results/blackjack-drawer-result-expanded-mobile.png' });
  await page.keyboard.press('Space');
  await expect(table).not.toBeVisible();
  expect((await drawer.boundingBox())!.height).toBe(collapsed!.height);
  // Leave the first result expanded; the next completed hand must start compact.
  await summary.click();
  await drawer.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await expect(panel.locator('.bj-hand-bet')).toHaveText('$10');
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(drawer).toBeVisible();
  await expect(table).not.toBeVisible();
  await expect(drawer.getByRole('button', { name: /^Change bet,/ })).toHaveAttribute('aria-expanded', 'false');
  await expect(drawer.getByRole('button', { name: 'Deal hand', exact: true })).toBeFocused();
  await drawer.getByRole('button', { name: 'Close betting' }).focus();
  await page.keyboard.press('Shift+Tab');
  await expect(summary).toBeFocused();
});

test('browser: an unaffordable repeated wager focuses Change bet and permits a smaller custom wager', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, 'Play'); await fixedShuffle(page);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: /^Change bet,/ }).click();
  await panel.getByLabel('Wager ($)').fill('300');
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  const drawer = panel.getByRole('dialog', { name: 'Your next wager' });
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$200');
  await expect(drawer.getByRole('button', { name: 'Deal hand', exact: true })).toBeDisabled();
  await expect(drawer.getByRole('button', { name: /^Change bet,/ })).toBeFocused();
  await page.keyboard.press('Enter');
  await drawer.getByLabel('Wager ($)').fill('37');
  await drawer.getByRole('button', { name: /^Change bet,/ }).click();
  await expect(drawer.getByLabel('Wager ($)')).not.toBeVisible();
  await drawer.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await expect(panel.locator('.bj-hand-bet')).toHaveText('$37');
});

test('browser: a round finishing in another mode waits to offer the next wager', async ({ page }) => {
  const panel = await pausedPlay(page);
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await finishTableMotion(page);
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  const learn = page.getByRole('tab', { name: /^Learn/ });
  await learn.click();
  await finishTableMotion(page);
  await expect(page.locator('dialog[open]')).toHaveCount(0);
  await expect(learn).toBeFocused();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(panel.getByRole('dialog', { name: 'Your next wager' })).toBeVisible();
});

test('browser: the betting drawer can reset an exhausted bankroll', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 568 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const fixed = randomFor(game => {
    if (game.phase !== 'player') return false;
    let settled = act(game, 'stand');
    while (settled.phase === 'dealer') settled = dealerStep(settled);
    return settled.hands[0].outcome === 'loss';
  });
  await open(page, 'Play'); await fixedShuffle(page, fixed.value);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: /^Change bet,/ }).click();
  await panel.getByLabel('Wager ($)').fill('500');
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  const next = panel.getByRole('dialog', { name: 'Your next wager' });
  await fullyInViewport(next);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$0');
  await expect(next.getByRole('button', { name: 'Deal hand', exact: true })).not.toBeVisible();
  await expect(next.getByRole('button', { name: 'Reset table · $500' })).toBeFocused();
  await fullyInViewport(next.getByRole('button', { name: 'Reset table · $500' }));
  await next.getByRole('button', { name: 'Reset table · $500' }).click();
  const initial = panel.getByRole('dialog', { name: 'Place your wager', exact: true });
  await expect(initial).toBeVisible();
  await expect(initial.getByLabel('Wager ($)')).not.toBeVisible();
  await expect(initial.getByRole('button', { name: 'Deal hand', exact: true })).toBeEnabled();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$500');
});

test('browser: insurance permits keyboard navigation, dismisses without a wager and resumes the decision', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'insurance' && !isBlackjack(game.dealer) && !isBlackjack(game.hands[0].cards));
  await page.setViewportSize({ width: 320, height: 568 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, 'Play');
  await fixedShuffle(page, fixed.value);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Insurance?', exact: true });
  await fullyInViewport(dialog);
  await expect(dialog.getByRole('button', { name: 'Decline insurance', exact: true })).toBeFocused();
  await dialog.getByRole('button', { name: 'Take insurance · $5', exact: true }).focus();
  await page.keyboard.press('Tab');
  expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(false);
  await page.keyboard.press('Shift+Tab');
  await expect(dialog.getByRole('button', { name: 'Take insurance · $5', exact: true })).toBeFocused();
  await page.screenshot({ path: 'test-results/blackjack-insurance-overlay-mobile.png' });
  await page.keyboard.press('Escape');
  await expect(dialog).not.toBeVisible();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  const resume = panel.getByRole('button', { name: 'Decide on insurance', exact: true });
  await expect(resume).toBeVisible();
  await expect(resume).toBeFocused();
  await page.getByRole('tab', { name: /^Learn/ }).click();
  await expect(dialog).not.toBeVisible();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await resume.click();
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Close insurance decision' }).click();
  await expect(resume).toBeFocused();
  await resume.click();
  await dialog.getByRole('button', { name: 'Take insurance · $5', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$485');
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeFocused();
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeVisible();
});

for (const viewport of [{ width: 390, height: 844 }, { width: 320, height: 568 }, { width: 844, height: 390 }, { width: 1440, height: 1000 }]) {
  test(`browser: floating insurance preserves hands, uncovers cards and expands help at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    const fixed = randomFor(game => game.phase === 'insurance' && isBlackjack(game.hands[0].cards));
    const theme = viewport.width === 320 || viewport.width === 1440 ? 'light' : 'dark';
    await page.addInitScript(value => localStorage.setItem('rabbithole-theme', value), theme);
    await page.setViewportSize(viewport);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await open(page, 'Play'); await fixedShuffle(page, fixed.value);
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    const panel = page.getByRole('tabpanel');
    await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Insurance?', exact: true });
    const geometry = () => panel.locator('.bj-play-table').evaluate(element => {
      const table = element.getBoundingClientRect();
      return {
        width: table.width, height: table.height,
        cards: Array.from(element.querySelectorAll('.bj-card')).map(card => {
          const box = card.getBoundingClientRect();
          return [box.left - table.left, box.top - table.top, box.width, box.height].map(value => Math.round(value * 100) / 100);
        }),
      };
    });
    const originalGeometry = await geometry();
    await fullyInViewport(dialog);
    await fullyInViewport(dialog.getByRole('button', { name: 'Decline insurance', exact: true }));
    await fullyInViewport(dialog.getByRole('button', { name: 'Take insurance · $5', exact: true }));
    const felt = await panel.locator('.bj-play-table').boundingBox();
    const bounds = (await dialog.boundingBox())!;
    expect(bounds.height).toBeLessThanOrEqual(170);
    expect(bounds.x).toBeGreaterThan(felt!.x);
    expect(bounds.y).toBeGreaterThan(felt!.y);
    expect(bounds.x + bounds.width).toBeLessThan(felt!.x + felt!.width);
    expect(bounds.y + bounds.height).toBeLessThan(felt!.y + felt!.height);
    expect(await dialog.evaluate(element => element.matches(':modal'))).toBe(false);
    expect(await page.evaluate(() => getComputedStyle(document.body).overflowY)).not.toBe('hidden');
    const cards = panel.locator('.bj-play-table .bj-card');
    for (const card of await cards.all()) {
      const cardBounds = (await card.boundingBox())!;
      expect(cardBounds.y + cardBounds.height <= bounds.y || cardBounds.y >= bounds.y + bounds.height).toBe(true);
      if (viewport.height >= 568) await fullyInViewport(card);
    }
    expect(await panel.locator('.bj-player-hand .bj-card').first().boundingBox()).toMatchObject({ width: 69, height: 97 });
    await expect(dialog.locator('#bj-insurance-details')).not.toBeVisible();
    await page.screenshot({ path: `test-results/blackjack-insurance-floating-${viewport.width}x${viewport.height}.png` });
    await dialog.getByRole('button', { name: 'How insurance works', exact: true }).click();
    await expect(dialog.locator('#bj-insurance-details')).toContainText('You have a natural.');
    await expect(dialog.locator('#bj-insurance-details')).toContainText('this separate wager loses');
    expect(await geometry()).toEqual(originalGeometry);
    await fullyInViewport(dialog.getByRole('button', { name: 'Decline insurance', exact: true }));
    await fullyInViewport(dialog.getByRole('button', { name: 'Take insurance · $5', exact: true }));
    const expanded = (await dialog.boundingBox())!;
    const expandedFelt = (await panel.locator('.bj-play-table').boundingBox())!;
    expect(expanded.height).toBeGreaterThan(bounds.height + 80);
    expect(expanded.y).toBeGreaterThan(expandedFelt.y);
    expect(expanded.y + expanded.height).toBeLessThan(expandedFelt.y + expandedFelt.height);
    await fullyInViewport(dialog);
    const copy = dialog.locator('.bj-insurance-copy');
    expect(await copy.evaluate(element => element.scrollHeight <= element.clientHeight + 1)).toBe(true);
    await page.screenshot({ path: `test-results/blackjack-insurance-floating-help-${viewport.width}x${viewport.height}.png` });
    if (viewport.width === 390) await page.screenshot({ path: 'test-results/blackjack-insurance-floating-help-mobile.png' });
    if (viewport.width === 844) {
      await page.setViewportSize({ width: 844, height: 240 });
      await dialog.evaluate(element => element.scrollIntoView({ block: 'center', behavior: 'instant' }));
      await fullyInViewport(dialog);
      await fullyInViewport(dialog.getByRole('button', { name: 'Decline insurance', exact: true }));
      await fullyInViewport(dialog.getByRole('button', { name: 'Take insurance · $5', exact: true }));
      await copy.focus(); await page.keyboard.press('End');
      await expect.poll(() => copy.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
      await page.setViewportSize(viewport);
    }
    await dialog.getByRole('button', { name: 'How insurance works', exact: true }).click();
    expect((await dialog.boundingBox())!.height).toBe(bounds.height);
    expect(await geometry()).toEqual(originalGeometry);
    await dialog.getByRole('button', { name: 'Close insurance decision' }).click();
    expect(await geometry()).toEqual(originalGeometry);
    await expect(panel.locator('.bj-felt-lettering')).toBeVisible();
    await panel.getByRole('button', { name: 'Decide on insurance', exact: true }).click();
    expect(await geometry()).toEqual(originalGeometry);
    await page.setViewportSize({ width: viewport.height, height: viewport.width });
    await dialog.evaluate(element => element.scrollIntoView({ block: 'center', behavior: 'instant' }));
    await fullyInViewport(dialog);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await dialog.getByRole('button', { name: 'Decline insurance', exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await expect(panel.getByRole('dialog', { name: 'Your next wager' })).toBeVisible();
  });
}

test('browser: floating insurance keeps its actions available when chips cannot cover insurance', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'insurance');
  await page.setViewportSize({ width: 320, height: 568 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, 'Play'); await fixedShuffle(page, fixed.value);
  await page.getByRole('button', { name: /^Change bet,/ }).click();
  await page.getByLabel('Wager ($)').fill('500');
  await page.getByRole('button', { name: 'Deal hand', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Insurance?', exact: true });
  await expect(dialog).toContainText('Not enough available chips for insurance.');
  await expect(dialog.getByRole('button', { name: 'Take insurance · $250', exact: true })).toBeDisabled();
  await fullyInViewport(dialog);
  await fullyInViewport(dialog.getByRole('button', { name: 'Decline insurance', exact: true }));
  await dialog.getByRole('button', { name: 'Decline insurance', exact: true }).click();
  await expect(dialog).not.toBeVisible();
});

test('browser: insurance allows page scrolling over the decision and background navigation', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'insurance');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, 'Play'); await fixedShuffle(page, fixed.value);
  await page.getByRole('button', { name: 'Deal hand', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Insurance?', exact: true });
  const bounds = (await dialog.boundingBox())!;
  const before = await page.evaluate(() => scrollY);
  await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
  await page.mouse.wheel(0, 180);
  await expect.poll(() => page.evaluate(() => scrollY)).toBeGreaterThan(before + 50);
  await expect(dialog).toBeVisible();
  await expect(page.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await page.getByRole('tab', { name: /^Learn/ }).click();
  await expect(dialog).not.toBeVisible();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(dialog).toBeVisible();
});

test('browser: an insurance deal finishing in another mode waits until Play is visible', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'insurance');
  const panel = await pausedPlay(page, fixed.value);
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await page.getByRole('tab', { name: /^Learn/ }).click();
  await finishTableMotion(page);
  await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(page.getByRole('dialog', { name: 'Insurance?', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Decline insurance', exact: true }).click();
  await finishTableMotion(page);
  await expect(page.getByRole('dialog', { name: 'Insurance?', exact: true })).not.toBeVisible();
});

async function pausedPlay(page: Page, random = .999999) {
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await page.clock.install({ time: new Date('2026-10-30T12:00:00Z') });
  await open(page, 'Play');
  await fixedShuffle(page, random);
  await page.clock.pauseAt(new Date('2026-10-30T13:00:00Z'));
  return page.getByRole('tabpanel');
}

async function finishTableMotion(page: Page) {
  const table = page.locator('#bj-panel-play .bj-play-table');
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
    await panel.getByRole('button', { name: 'Deal hand', exact: true }).focus();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeFocused();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.locator('.bj-player-hand').getByRole('img')).toHaveCount(3);
    await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(panel.getByRole('button', { name: 'Stand', exact: true })).toBeFocused();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.locator('.bj-round-summary')).toBeVisible();
    await expect(panel.getByRole('button', { name: 'Deal hand', exact: true })).toBeFocused();
  });
}

for (const motion of ['no-preference', 'reduce'] as const) {
  test(`browser: keyboard focus reaches insurance and split decisions with ${motion} motion`, async ({ page }) => {
    const insurance = randomFor(game => game.phase === 'insurance' && !isBlackjack(game.dealer) && !isBlackjack(game.hands[0].cards));
    const panel = await pausedPlay(page, insurance.value);
    await page.emulateMedia({ reducedMotion: motion });
    await panel.getByRole('button', { name: 'Deal hand', exact: true }).focus();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.getByRole('button', { name: 'Decline insurance', exact: true })).toBeFocused();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeFocused();
    await panel.getByRole('button', { name: 'Stand', exact: true }).focus();
    await page.keyboard.press('Enter'); await finishTableMotion(page);
    await resetTable(panel);
    const split = randomFor(game => game.phase === 'player' && game.hands[0].cards.every(card => card.rank === '8'));
    await fixedShuffle(page, split.value);
    await panel.getByRole('button', { name: 'Deal hand', exact: true }).focus();
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
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).evaluate(button => { button.click(); button.click(); });
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
  await expect(panel.locator('.bj-round-summary')).toHaveCount(0);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await finishTableMotion(page);
  await expect(panel.locator('.bj-round-summary')).toContainText('+$10');
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$510');
});

test('browser: motion holds natural payouts until the deal and reveal finish', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'settled' && game.hands[0].outcome === 'blackjack');
  const panel = await pausedPlay(page, fixed.value);
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await expect(panel.locator('.bj-round-summary')).toHaveCount(0);
  await expect(panel.getByRole('button', { name: /^Deal/ })).toHaveCount(0);
  await expect(panel.locator('.bj-player-hand')).not.toContainText('Blackjack');
  await finishTableMotion(page);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$515');
  await expect(panel.locator('.bj-round-summary')).toContainText('+$15');
  await expect(panel.locator('.bj-player-hand')).toContainText('Blackjack · 21');
  // A following round has no leftover result or payout timer.
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$505');
  await expect(panel.locator('.bj-round-summary')).toHaveCount(0);
});

test('browser: motion keeps split Ace cards separate and survives mode switches', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'player' && game.hands[0].cards.every(card => card.rank === 'A'));
  const panel = await pausedPlay(page, fixed.value);
  await page.setViewportSize({ width: 320, height: 844 });
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
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
  await expect(panel.locator('.bj-round-summary')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('browser: reduced motion skips current and future animation delays', async ({ page }) => {
  const panel = await pausedPlay(page);
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
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
  await resetTable(panel);
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
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
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await expect(panel.locator('.bj-dealer').getByRole('img', { name: 'Face-down card' })).toBeVisible();
  await expect(panel.getByRole('img', { name: '5 of clubs' })).toHaveCount(0);
  await panel.getByRole('button', { name: 'Hit', exact: true }).click();
  await expect(panel.locator('.bj-feedback')).toContainText('Hard 6 vs 3');
  await expect(panel.locator('.bj-player-hand')).toContainText('Hard 12');
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(panel.locator('.bj-feedback')).toContainText('Best move: Hit.');
  await expect(panel.locator('.bj-feedback')).toContainText('Hard 12 vs 3');
  await expect(panel.locator('.bj-round-summary')).toContainText('+$10');
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$510');
  await expect(panel.locator('.bj-player-hand .bj-card')).toHaveCount(3);
  await expect(panel.getByRole('img', { name: '5 of clubs' })).toBeVisible();
  await panel.getByRole('button', { name: 'Close betting' }).click();
  await page.screenshot({ path: 'test-results/blackjack-play-desktop.png', fullPage: true });
  await panel.getByRole('switch', { name: 'Coach' }).click();
  await expect(panel.locator('.bj-feedback')).toHaveCount(0);
  await page.reload(); await page.getByRole('tab', { name: /^Play/ }).click();
  await expect(panel.getByRole('switch', { name: 'Coach' })).not.toBeChecked();
  // Reload restores native randomness; fix the new page's shuffle after hydration.
  await fixedShuffle(page);
  await panel.getByRole('button', { name: 'Place your wager', exact: true }).click();
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(panel.locator('.bj-round-summary')).toBeVisible();
  await expect(panel.locator('.bj-feedback')).toHaveCount(0);
  await resetTable(panel);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$500');
});

test('browser: split hands advance visibly and settle the expected balance on mobile', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'player' && game.hands[0].cards.every(card => card.rank === '8'));
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, 'Play');
  await fixedShuffle(page, fixed.value);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
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
  await expect(panel.locator('.bj-round-summary')).toBeVisible();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText(money(expected.bankroll));
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('browser: insurance comes before peek and is settled independently', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'insurance' && ['10', 'J', 'Q', 'K'].includes(game.dealer[1].rank) && game.hands[0].cards.every(card => card.rank !== 'A'));
  await open(page, 'Play'); await fixedShuffle(page, fixed.value);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await expect(panel.getByRole('heading', { name: 'Insurance?', exact: true })).toBeVisible();
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toHaveCount(0);
  await expect(panel.locator('.bj-dealer .bj-total')).toHaveText('A showing');
  await panel.getByRole('button', { name: 'Take insurance · $5' }).click();
  await expect(panel.locator('.bj-round-summary')).toContainText('Even');
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$500');
  await expect(panel.locator('.bj-feedback')).toContainText('Best move: Decline insurance.');
  await expect(panel.locator('.bj-dealer .bj-total')).toHaveText('Blackjack · 21');
  await expect(panel.locator('.bj-table-status')).toContainText('The round ends immediately');
  await expect(panel.getByRole('button', { name: /^(Hit|Stand)$/ })).toHaveCount(0);
  await expect(panel.locator('.bj-player-hand')).toContainText('Hand return $0 · Hand net −$10');
  await panel.locator('.bj-round-summary').click();
  const breakdown = panel.getByRole('table', { name: 'Round wager breakdown' });
  const row = (name: string | RegExp) => breakdown.getByRole('row').filter({ has: page.getByRole('rowheader', { name, exact: true }) });
  await expect(row('Main bet').getByRole('cell')).toHaveText(['$10', '$0', '−$10']);
  await expect(row(/^Insurance/).getByRole('cell')).toHaveText(['$5', '$15', '+$10']);
  await expect(row('Total').getByRole('cell')).toHaveText(['$15', '$15', '$0']);
  await page.screenshot({ path: 'test-results/blackjack-insurance-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 320, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await panel.getByRole('dialog', { name: 'Your next wager' }).screenshot({ path: 'test-results/blackjack-insurance-mobile.png' });
  // Declining changes only the side bet; the same dealer blackjack still ends play.
  await resetTable(panel);
  await fixedShuffle(page, fixed.value);
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await panel.getByRole('button', { name: 'Decline insurance', exact: true }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$490');
  await expect(panel.getByRole('button', { name: /^(Hit|Stand)$/ })).toHaveCount(0);
  await panel.locator('.bj-round-summary').click();
  await expect(row(/^Insurance/)).toHaveCount(0);
  await expect(row('Total').getByRole('cell')).toHaveText(['$10', '$0', '−$10']);
});

test('browser: losing insurance leaves the main hand playable and appears in the final total', async ({ page }) => {
  const fixed = randomFor(game => game.phase === 'insurance' && !isBlackjack(game.dealer) && !isBlackjack(game.hands[0].cards));
  await open(page, 'Play'); await fixedShuffle(page, fixed.value);
  const panel = page.getByRole('tabpanel');
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await panel.getByRole('button', { name: 'Take insurance · $5' }).click();
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$485');
  await expect(panel.locator('.bj-insurance-result')).toHaveText('Insurance lost: $5. Your main bet is still in play.');
  await expect(panel.locator('.bj-dealer').getByRole('img', { name: 'Face-down card' })).toBeVisible();
  await expect(panel.getByRole('button', { name: 'Hit', exact: true })).toBeEnabled();
  await expect(panel.locator('.bj-round-summary')).toHaveCount(0);
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  let expected = act(insuranceDecision(fixed.game, true), 'stand');
  while (expected.phase === 'dealer') expected = dealerStep(expected);
  await expect(panel.getByTestId('blackjack-bankroll')).toHaveText(money(expected.bankroll));
  await panel.locator('.bj-round-summary').click();
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
  await panel.getByRole('button', { name: /^Change bet,/ }).click();
  for (const amount of ['-1', '0', '4', '5.50', '501', '10000']) {
    await panel.getByLabel('Wager ($)', { exact: true }).fill(amount);
    await expect(panel.getByRole('button', { name: 'Deal hand', exact: true })).toBeDisabled();
    await expect(panel.getByTestId('blackjack-bankroll')).toHaveText('$500');
  }
  await panel.getByRole('button', { name: 'Bet $5', exact: true }).click();
  await expect(panel.getByRole('button', { name: 'Deal hand', exact: true })).toBeEnabled();
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
  await panel.getByRole('button', { name: 'Place your wager', exact: true }).click();
  await panel.getByRole('button', { name: 'Deal hand', exact: true }).click();
  await panel.getByRole('button', { name: 'Stand', exact: true }).click();
  await expect(panel.locator('.bj-round-summary')).toBeVisible();
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
  await page.getByRole('button', { name: 'Place your wager', exact: true }).click();
  await expect(page.getByRole('tabpanel').getByRole('button', { name: 'Deal hand' })).toBeEnabled();
});
