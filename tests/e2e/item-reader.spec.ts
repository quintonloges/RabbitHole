import { type Page } from '@playwright/test';
import { test, expect } from './auth-fixture';
import { randomUUID } from 'node:crypto';
import type { BookSection, Item, Note } from '../../apps/web/lib/types';

const bookId = randomUUID();
const opening = 'TEST FIXTURE: The passage begins here, before any record-management controls.';
const body = opening.replaceAll(' ', '\t') + '\n\n' + 'This\tsupplied paragraph describes\ncuriosity and careful obser-\nvation. '.repeat(90);
const pages: BookSection[] = Array.from({ length: 200 }, (_, index) => ({ id: randomUUID(), documentId: bookId, itemId: randomUUID(), title: `Page ${index + 1}`, order: index, startPage: index + 1, endPage: index + 1, locator: `page=${index + 1}` }));
function base(title: string, patch: Partial<Item> = {}): Item {
  return { isPublic: false, canPublish: false, id: randomUUID(), title, kind: 'article', canonicalUrl: 'https://example.org/fixture', provider: 'manual', ingestedAt: '2026-01-01T00:00:00Z', textProvenance: 'metadata-only', state: { saved: false, read: false, understood: false, dismissed: false, feedback: 0, progress: 0 }, topics: [], notes: [], related: [], ...patch };
}
const first = base('Page 165', { id: pages[164].itemId, documentId: bookId, sectionId: pages[164].id, kind: 'book', provider: 'local-upload', canonicalUrl: 'local://fixture/165', body, excerpt: opening, hasText: true, textProvenance: 'personal-upload', document: { id: bookId, title: 'TEST FIXTURE: A book worth reading', author: 'Fixture author', format: 'PDF', sectionType: 'page', startPage: 165, endPage: 165, pageCount: 200, isSuperseded: false } });
const second = { ...first, id: pages[165].itemId, sectionId: pages[165].id, title: 'Page 166', document: { ...first.document!, startPage: 166, endPage: 166 } };
const video = base('TEST FIXTURE: A video to watch', { kind: 'video', provider: 'youtube', embedUrl: 'https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ' });
const paper = base('TEST FIXTURE: A scholarly abstract', { kind: 'paper', provider: 'openalex', body: opening, excerpt: opening, hasText: true, textProvenance: 'provider-abstract', doi: '10.1234/fixture' });
const link = base('TEST FIXTURE: Link only');

async function fixture(page: Page, ai = false) {
  const records = new Map([first, second, video, paper, link].map(item => [item.id, { ...item, notes: [] as Note[] }]));
  await page.route('**/api/settings', route => route.fulfill({ json: { theme: 'light' } }));
  await page.route('**/api/topics', route => route.fulfill({ json: [] }));
  await page.route('**/api/ai/status', route => route.fulfill({ json: { configured: ai, enabled: ai, privacy: 'TEST FIXTURE: AI receives the selected source text.' } }));
  await page.route(`**/api/library/books/${bookId}/sections`, route => route.fulfill({ json: pages }));
  await page.route(/\/api\/items\/[^/]+$/, route => route.fulfill({ json: records.get(route.request().url().split('/').pop()!) }));
  await page.route(/\/api\/items\/[^/]+\/state$/, route => {
    const id = route.request().url().split('/').at(-2)!;
    const record = records.get(id)!;
    record.state = { ...record.state, ...route.request().postDataJSON() };
    return route.fulfill({ json: record.state });
  });
  await page.route(/\/api\/items\/[^/]+\/notes$/, route => {
    const id = route.request().url().split('/').at(-2)!;
    const note = { id: randomUUID(), itemId: id, ...route.request().postDataJSON(), createdAt: '2026-01-01T12:00:00Z' };
    records.get(id)!.notes.push(note);
    return route.fulfill({ json: note });
  });
  return records;
}

async function selectText(page: Page, selector: string, length: number) {
  await page.locator(selector).evaluate((element, count) => {
    const range = document.createRange();
    range.setStart(element.firstChild!, 0);
    range.setEnd(element.firstChild!, Math.min(count, element.firstChild!.textContent!.length));
    const selection = window.getSelection()!;
    selection.removeAllRanges(); selection.addRange(range);
  }, length);
}

test('reading stays above the fold while contents and secondary actions remain accessible', async ({ page }) => {
  await fixture(page);
  await page.route(`**/api/library/books/${bookId}/sections`, route => route.fulfill({ status: 503, json: { detail: 'TEST FIXTURE: navigation temporarily unavailable' } }), { times: 1 });
  await page.goto(`/admin/items/${first.id}`);
  await expect(page.locator('.reader-body')).toContainText(opening);
  await expect(page.locator('.reader-lede')).toHaveCount(0);
  const reflowed = await page.locator('.reader-body').textContent();
  expect(reflowed).toContain(opening + '\n\nThis supplied paragraph describes curiosity');
  expect(reflowed).not.toContain('\t');
  expect(reflowed!.replace(/\s+/g, ' ').trim()).toBe(body.replace(/\s+/g, ' ').trim());
  await expect(page.getByRole('heading', { name: 'TEST FIXTURE: A book worth reading — page 165' })).toBeVisible();
  const navigation = page.getByRole('navigation', { name: 'Document navigation', exact: true });
  await navigation.getByRole('button', { name: 'Retry navigation' }).click();
  await expect(navigation.getByRole('link', { name: 'Next page', exact: true })).toHaveAttribute('href', `/admin/items/${second.id}`);
  for (const width of [1440, 1024, 390]) {
    await page.setViewportSize({ width, height: 900 });
    expect((await page.locator('.reader-body').boundingBox())!.y).toBeLessThan(650);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: `test-results/item-reader-${width}.png` });
  }
  await page.getByRole('button', { name: 'Contents', exact: true }).click();
  const contents = page.getByRole('dialog', { name: 'Table of contents', exact: true });
  await expect(contents).toBeVisible();
  expect((await contents.boundingBox())!.height).toBeLessThan(900 * 0.82);
  await expect(contents.getByRole('link', { name: 'Page 165', exact: true })).toHaveAttribute('aria-current', 'page');
  await contents.getByLabel('Find in pages', { exact: true }).fill('200');
  await expect(contents.getByRole('link', { name: 'Page 200', exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(contents).not.toBeVisible();
  await expect(page.getByRole('button', { name: 'Contents', exact: true })).toBeFocused();
  await page.locator('.reader-more-actions > summary').click();
  await expect(page.getByRole('button', { name: 'I understand this', exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('button', { name: 'I understand this', exact: true })).not.toBeVisible();
  await page.getByRole('tab', { name: 'Details', exact: true }).click();
  await page.getByLabel('Preserve PDF line breaks').check();
  expect(await page.locator('.reader-body').textContent()).toBe(body);
  await navigation.getByRole('link', { name: 'Next page', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/admin/items/${second.id}$`));
  await expect(page.locator('.reader-body')).toContainText(opening);
  await page.getByRole('tab', { name: 'Details', exact: true }).click();
  await expect(page.getByLabel('Preserve PDF line breaks')).toBeChecked();
  expect(await page.locator('.reader-body').textContent()).toBe(body);
});

test('note drafts and quotes stay with their item and survive a failed save', async ({ page }) => {
  const records = await fixture(page);
  await page.goto(`/admin/items/${first.id}`);
  await page.getByLabel('Your note').fill('TEST FIXTURE: Keep this draft with page 165.');
  await selectText(page, '.reader-body', opening.length);
  await page.getByRole('button', { name: 'Quote in note', exact: true }).click();
  await expect(page.locator('#panel-notes .reader-note-quote')).toContainText(opening);
  await page.getByRole('navigation', { name: 'Document navigation', exact: true }).getByRole('link', { name: 'Next page', exact: true }).click();
  await expect(page.getByLabel('Your note')).toHaveValue('');
  await expect(page.locator('#panel-notes .reader-note-quote')).toHaveCount(0);
  await page.getByRole('navigation', { name: 'Document navigation', exact: true }).getByRole('link', { name: 'Previous page', exact: true }).click();
  await expect(page.getByLabel('Your note')).toHaveValue('TEST FIXTURE: Keep this draft with page 165.');
  await page.route(`**/api/items/${first.id}/notes`, route => route.fulfill({ status: 503, json: { detail: 'TEST FIXTURE: note save unavailable' } }), { times: 1 });
  await page.getByRole('button', { name: 'Save note', exact: true }).click();
  await expect(page.getByRole('tabpanel', { name: 'Notes', exact: true }).getByRole('alert')).toContainText('note save unavailable');
  await expect(page.getByLabel('Your note')).toHaveValue('TEST FIXTURE: Keep this draft with page 165.');
  let complete!: () => void;
  const pending = new Promise<void>(resolve => { complete = resolve; });
  await page.route(`**/api/items/${first.id}/notes`, async route => { await pending; await route.fallback(); }, { times: 1 });
  await page.getByRole('button', { name: 'Save note', exact: true }).click();
  try {
    await expect(page.getByLabel('Your note')).toBeDisabled();
    await selectText(page, '.reader-body', opening.length);
    await expect(page.getByRole('button', { name: 'Quote in note', exact: true })).toBeDisabled();
  } finally { complete(); }
  await expect(page.getByLabel('Your note')).toHaveValue('');
  expect(records.get(first.id)!.notes).toHaveLength(1);
  expect(records.get(first.id)!.notes[0].quote).toBe(opening);
  expect(records.get(second.id)!.notes).toHaveLength(0);
  await page.reload();
  await expect(page.locator('.reader-note')).toContainText('Keep this draft with page 165.');
  await expect(page.getByLabel('Your note')).toHaveValue('');
});

test('highlights accept only source selections and report persistence without duplicating a note draft', async ({ page }) => {
  const records = await fixture(page);
  await page.goto(`/admin/items/${first.id}`);
  await expect(page.locator('.reader-body')).toBeVisible();
  await selectText(page, '.reader-title h1', 12);
  await expect(page.getByRole('region', { name: 'Selected passage' })).toHaveCount(0);
  await selectText(page, '.reader-body', 4100);
  await page.getByRole('button', { name: 'Highlight selected text', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Selected passage' }).getByRole('alert')).toContainText('shorter passage');
  expect(records.get(first.id)!.notes).toHaveLength(0);
  await selectText(page, '.reader-body', opening.length);
  await page.getByRole('button', { name: 'Highlight selected text', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Selected passage' }).getByRole('status')).toHaveText('Highlight saved.');
  expect(records.get(first.id)!.notes).toHaveLength(1);
  await expect(page.locator('.reader-note-editor .reader-note-quote')).toHaveCount(0);
  await page.getByRole('button', { name: 'Close selection tools' }).click();
  await expect(page.getByRole('region', { name: 'Selected passage' })).toHaveCount(0);
});

test('media, abstracts and link-only items present the content they actually contain', async ({ page }) => {
  await fixture(page);
  let playerRequests = 0;
  await page.route('https://www.youtube-nocookie.com/**', route => { playerRequests++; return route.fulfill({ contentType: 'text/html', body: '<p>TEST FIXTURE: consented player</p>' }); });
  await page.goto(`/admin/items/${video.id}`);
  await expect(page.getByRole('button', { name: 'Load player' })).toBeVisible();
  expect(playerRequests).toBe(0);
  await expect(page.locator('.reader-content-notice')).toHaveCount(0);
  await page.getByRole('button', { name: 'Load player' }).click();
  await expect(page.locator('iframe')).toBeVisible();
  await expect.poll(() => playerRequests).toBe(1);
  await page.goto(`/admin/items/${paper.id}`);
  await expect(page.getByRole('heading', { name: 'Abstract', exact: true })).toBeVisible();
  await expect(page.locator('.reader-body')).toHaveText(opening);
  await expect(page.locator('.reader-lede')).toHaveCount(0);
  await page.getByRole('tab', { name: 'Details', exact: true }).click();
  await expect(page.getByText('DOI: 10.1234/fixture', { exact: false })).toBeVisible();
  await page.goto(`/admin/items/${link.id}`);
  await expect(page.getByRole('link', { name: 'Open original source', exact: true })).toHaveAttribute('href', link.canonicalUrl);
  await expect(page.locator('.reader-content-notice')).toContainText('The text isn’t available here.');
});

test('an AI response that finishes after page navigation never appears on the next item', async ({ page }) => {
  await fixture(page, true);
  let complete!: () => void;
  const pending = new Promise<void>(resolve => { complete = resolve; });
  await page.route(`**/api/items/${first.id}/ai`, async route => { await pending; await route.fulfill({ json: { text: 'TEST FIXTURE: synthesis for page 165 only', evidenceItemIds: [first.id] } }); });
  await page.goto(`/admin/items/${first.id}`);
  await page.getByRole('tab', { name: 'Explore', exact: true }).click();
  await page.locator('.reader-ai-tools > summary').click();
  const answer = page.waitForResponse(response => response.url().endsWith(`/api/items/${first.id}/ai`));
  await page.getByRole('button', { name: 'Summarize', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Generating');
  try {
    await page.getByRole('navigation', { name: 'Document navigation', exact: true }).getByRole('link', { name: 'Next page', exact: true }).click();
    await expect(page.getByRole('heading', { name: /page 166$/ })).toBeVisible();
  } finally { complete(); }
  await answer;
  await page.getByRole('tab', { name: 'Explore', exact: true }).click();
  await page.locator('.reader-ai-tools > summary').click();
  await expect(page.getByRole('button', { name: 'Summarize', exact: true })).toBeEnabled();
  await expect(page.getByText('TEST FIXTURE: synthesis for page 165 only', { exact: true })).toHaveCount(0);
});
