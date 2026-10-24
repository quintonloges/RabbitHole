import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import type { Item, Topic } from '../../apps/web/lib/types';

const mind: Topic = { id: randomUUID(), name: 'TEST FIXTURE Minds', description: '', keywords: '', aliases: '', weight: 3, pinned: false, curiosityOnly: false, enabled: true };
const empty: Topic = { ...mind, id: randomUUID(), name: 'TEST FIXTURE Empty topic' };
function item(title: string, patch: Partial<Item> = {}): Item {
  return { id: randomUUID(), title, kind: 'article', provider: 'manual', canonicalUrl: 'https://example.org/fixture', ingestedAt: '2026-01-01T00:00:00Z', textProvenance: 'metadata-only', hasText: false, state: { saved: false, read: false, understood: false, dismissed: false, progress: 0, feedback: 0 }, topics: [mind], ...patch };
}
const pdf = { id: randomUUID(), title: 'TEST FIXTURE: Field notes', author: 'Fixture author', format: 'PDF', sectionType: 'page' as const, startPage: 116, endPage: 116, pageCount: 369, isSuperseded: false };
const mixed = [
  item('Page 116', { kind: 'book', provider: 'local-upload', documentId: pdf.id, document: pdf, hasText: true, textProvenance: 'personal-upload', canonicalUrl: 'local://document/fixture/page/116', excerpt: 'A supplied passage about memory and how people describe an experience.', reason: 'Matches your interest in minds.', rationale: { version: 2, kind: 'daily', summary: 'Matches your interest in minds.', detail: 'No format preference applied.', evidence: [] } }),
  item('Text from index_split_018.xhtml', { kind: 'book', documentId: randomUUID(), document: { ...pdf, id: randomUUID(), title: 'TEST FIXTURE: Collected essays', format: 'EPUB', sectionType: 'section', sectionNumber: 19, startPage: null, endPage: null, pageCount: 0 }, hasText: true, excerpt: 'The author supplies these notes on perception.' }),
  item('TEST FIXTURE: A paper', { kind: 'paper', provider: 'openalex', hasText: true, textProvenance: 'provider-abstract', excerpt: 'This supplied abstract describes a controlled experiment.' }),
  item('TEST FIXTURE: A link', { excerpt: 'Metadata supplied by the source.' }),
  item('TEST FIXTURE: A video', { kind: 'video', provider: 'youtube', embedUrl: 'https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ' }),
  item('TEST FIXTURE: Audio', { kind: 'audio', provider: 'soundcloud', embedUrl: 'https://w.soundcloud.com/player/?url=https%3A%2F%2Fapi.soundcloud.com%2Ftracks%2F123' }),
  item('TEST FIXTURE: Original only', { kind: 'book', documentId: pdf.id, document: { ...pdf, sectionType: 'document', startPage: null, endPage: null }, hasText: false }),
];

async function mockFeed(page: Page) {
  await page.route('**/api/topics', route => route.fulfill({ json: [mind, empty] }));
  await page.route('**/api/holes', route => route.fulfill({ json: [] }));
  await page.route('**/api/settings', route => route.fulfill({ json: { theme: 'light' } }));
  await page.route('**/api/discover/today', route => route.fulfill({ json: { id: randomUUID(), day: '2026-01-01', items: mixed, total: mixed.length } }));
}

test('mixed rows identify documents, actual text availability and source excerpts without opening drawers', async ({ page }) => {
  await mockFeed(page);
  await page.goto('/');
  const rows = page.locator('.story-row');
  await expect(rows).toHaveCount(7);
  await expect(rows.nth(0).getByRole('heading', { name: 'TEST FIXTURE: Field notes — page 116' })).toBeVisible();
  await expect(rows.nth(0)).toContainText('PDF page');
  await expect(rows.nth(0)).toContainText('Page 116 of 369');
  await expect(rows.nth(0).locator('.item-excerpt')).toBeVisible();
  await expect(rows.nth(0)).not.toContainText('local-upload');
  await expect(rows.nth(1)).toContainText('EPUB section');
  await expect(rows.nth(1).getByRole('heading', { name: 'TEST FIXTURE: Collected essays — section 19' })).toBeVisible();
  await expect(rows.nth(1)).not.toContainText('index_split_018.xhtml');
  await expect(rows.nth(2)).toContainText('Abstract only');
  await expect(rows.nth(3)).toContainText('Excerpt only');
  await expect(rows.nth(4).locator('.item-format')).toContainText('Video');
  await expect(rows.nth(5).locator('.item-format')).toContainText('Audio');
  await expect(rows.nth(6)).toContainText('No extracted text');
  await expect(page.getByText('Preview', { exact: true })).toHaveCount(0);
  const trigger = rows.nth(0).getByRole('button', { name: 'Why this?', exact: true });
  const before = await trigger.boundingBox();
  await trigger.click();
  const explanation = page.getByRole('dialog', { name: 'Why this was selected' });
  await expect(explanation).toBeVisible();
  await expect(explanation).toContainText('Matches your interest in minds.');
  await expect(explanation).not.toContainText('No format preference applied.');
  expect(await trigger.boundingBox()).toEqual(before);
  await page.keyboard.press('Escape');
  await expect(explanation).not.toBeVisible();
  await expect(trigger).toBeFocused();
  await trigger.click();
  await page.mouse.wheel(0, 300);
  await expect(explanation).not.toBeVisible();
  await page.evaluate(() => window.scrollTo(0, 0));
  for (const width of [1440, 1024, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: `test-results/discovery-context-${width}.png`, fullPage: true });
  }
  await trigger.click();
  await expect(explanation).toBeVisible();
  const popup = await explanation.boundingBox();
  expect(popup!.x).toBeGreaterThanOrEqual(0);
  expect(popup!.x + popup!.width).toBeLessThanOrEqual(390);
  await explanation.getByRole('button', { name: 'Close explanation' }).click();
  await expect(explanation).not.toBeVisible();
});

test('topic browsing reaches the collection, paginates and survives navigation without regenerating the daily picks', async ({ page }) => {
  await mockFeed(page);
  const catalog = Array.from({ length: 34 }, (_, index) => item(`TEST FIXTURE: Topic source ${index + 1}`));
  const requested: string[] = [];
  await page.route('**/api/items?*', route => {
    const url = new URL(route.request().url());
    requested.push(url.search);
    expect(url.searchParams.get('dismissed')).toBe('false');
    expect(url.searchParams.get('order')).toBe('reading');
    const records = url.searchParams.get('topicId') === mind.id ? catalog : [];
    const offset = Number(url.searchParams.get('offset'));
    return route.fulfill({ json: { items: records.slice(offset, offset + 30), total: records.length } });
  });
  await page.route('**/api/discover/refresh-edition', () => { throw new Error('Browsing must not replace the daily edition'); });
  await page.goto('/');
  await page.getByRole('combobox', { name: 'Browse topic' }).selectOption(mind.id);
  await expect(page).toHaveURL(`http://127.0.0.1:3001/?topic=${mind.id}`);
  await expect(page.getByRole('heading', { name: mind.name, exact: true })).toBeVisible();
  await expect(page.locator('.story-row')).toHaveCount(30);
  await expect(page.getByRole('link', { name: 'TEST FIXTURE: Topic source 1', exact: true })).toBeVisible();
  await expect(page.getByText('All items in this topic', { exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Next', exact: true }).click();
  await expect(page.locator('.story-row')).toHaveCount(4);
  await page.reload();
  await expect(page.getByRole('combobox', { name: 'Browse topic' })).toHaveValue(mind.id);
  await expect(page.locator('.story-row').first()).toContainText('Topic source 31');
  await page.goBack();
  await expect(page.locator('.story-row')).toHaveCount(30);
  await page.getByRole('combobox', { name: 'Browse topic' }).selectOption(empty.id);
  await expect(page.getByRole('heading', { name: 'No items in this topic yet' })).toBeVisible();
  await page.locator('.daily-picks').click();
  await expect(page.locator('.story-row')).toHaveCount(7);
  expect(requested.some(query => query.includes('offset=30'))).toBe(true);
});
