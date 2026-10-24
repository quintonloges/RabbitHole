import { test, expect, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import type { Book, BookSection, Item } from '../../apps/web/lib/types';

function documentFixture() {
  const book: Book = { id: randomUUID(), title: 'TEST FIXTURE: A large private PDF', author: 'Fixture author', fileName: 'large.pdf', status: 'ready', pageCount: 369, createdAt: '2026-01-01T12:00:00Z' };
  const section = (title: string, locator: string, startPage: number, endPage: number, order: number): BookSection => ({ id: randomUUID(), documentId: book.id, itemId: randomUUID(), title, order, locator, startPage, endPage });
  const pages = Array.from({ length: 369 }, (_, index) => section(`Page ${index + 1}`, `page=${index + 1}`, index + 1, index + 1, index));
  const chapters = Array.from({ length: 34 }, (_, index) => section(`TEST FIXTURE: Chapter ${index + 1}`, `bookmark:${index * 10 + 1}-${index * 10 + 10}`, index * 10 + 1, index * 10 + 10, 369 + index));
  return { book, pages, chapters, sections: [...pages, ...chapters] };
}

async function openBook(page: Page, book: Book) {
  await page.route('**/api/library/books', route => route.fulfill({ json: [book] }));
  await page.goto('/library');
  await page.getByRole('tab', { name: 'Books & files', exact: true }).click();
  const card = page.locator('.document-card').filter({ has: page.getByRole('heading', { name: book.title, exact: true }) });
  await card.getByRole('button', { name: 'Sections', exact: true }).click();
  return card;
}

test('large PDFs put chapters first and keep page navigation and document controls compact', async ({ page }) => {
  const fixture = documentFixture();
  await page.route(`**/api/library/books/${fixture.book.id}/sections`, route => route.fulfill({ json: fixture.sections }));
  const card = await openBook(page, fixture.book);
  await expect(card.getByRole('heading', { name: 'Chapters & ranges (34)', exact: true })).toBeVisible();
  await expect(card.locator('.section-list a')).toHaveCount(16); // Eight chapters plus eight pages in a closed details element.
  await expect(card.getByRole('link', { name: 'Read', exact: true })).toHaveCount(8);
  await expect(card.getByRole('heading', { name: fixture.chapters[0].title, exact: true })).toBeVisible();
  await expect(card.getByRole('heading', { name: 'Page 1', exact: true })).not.toBeVisible();
  await expect(card.getByRole('link', { name: 'Open original file', exact: true })).toBeVisible();
  await expect(card.getByRole('button', { name: 'Rebuild sections', exact: true })).toBeVisible();
  await page.screenshot({ path: 'test-results/document-sections-desktop.png', fullPage: true });
  await card.locator('summary').filter({ hasText: /^Mark a page range$/ }).click();
  await expect(card.getByRole('button', { name: 'Add range', exact: true })).toBeVisible();
  await card.getByLabel('Find in chapters & ranges', { exact: true }).fill('Chapter 34');
  await expect(card.getByRole('heading', { name: fixture.chapters[33].title, exact: true })).toBeVisible();
  await card.locator('summary').filter({ hasText: /^Pages \(369\)$/ }).click();
  await card.getByLabel('Find in pages', { exact: true }).fill('369');
  await expect(card.getByRole('heading', { name: 'Page 369', exact: true })).toBeVisible();
  await expect(card.getByText('Pages 369–369', { exact: true })).toHaveCount(0);
  expect(await page.locator('main').innerText()).not.toMatch(/Ã|Â|â€|âœ|ï¿/);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: 'test-results/document-sections-mobile.png', fullPage: true });
});

test('section loading, failure, retry and rebuild remain explicit and keep controls available', async ({ page }) => {
  const fixture = documentFixture();
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  await page.route(`**/api/library/books/${fixture.book.id}/sections`, async route => {
    await pending;
    await route.fulfill({ status: 503, json: { detail: 'TEST FIXTURE: sections temporarily unavailable' } });
  }, { times: 1 });
  const card = await openBook(page, fixture.book);
  try {
    await expect(card.getByRole('status')).toHaveText('Loading sections…');
    await expect(card.getByRole('link', { name: 'Open original file', exact: true })).toBeVisible();
    await expect(card.getByText(/No chapters were detected/)).toHaveCount(0);
  } finally { release(); }
  await expect(card.getByRole('alert')).toContainText('TEST FIXTURE: sections temporarily unavailable');
  await page.route(`**/api/library/books/${fixture.book.id}/sections`, route => route.fulfill({ json: fixture.sections }));
  await card.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(card.getByRole('heading', { name: fixture.chapters[0].title, exact: true })).toBeVisible();
  let finish!: () => void;
  const rebuilding = new Promise<void>(resolve => { finish = resolve; });
  await page.route(`**/api/library/books/${fixture.book.id}/retry`, async route => {
    await rebuilding;
    await route.fulfill({ json: { document: fixture.book, sections: [{ ...fixture.chapters[0], title: 'TEST FIXTURE: Rebuilt chapter' }] } });
  });
  await card.getByRole('button', { name: 'Rebuild sections', exact: true }).click();
  try {
    await expect(card.getByRole('button', { name: 'Rebuilding sections…', exact: true })).toBeDisabled();
    await expect(card.getByRole('button', { name: 'Edit details', exact: true })).toBeDisabled();
  } finally { finish(); }
  await expect(card.getByRole('heading', { name: 'TEST FIXTURE: Rebuilt chapter', exact: true })).toBeVisible();
  await expect(card.getByRole('alert')).toHaveCount(0);
});

test('reupload refreshes an expanded document instead of retaining stale sections', async ({ page }) => {
  const fixture = documentFixture();
  let uploaded = false;
  await page.route(`**/api/library/books/${fixture.book.id}/sections`, route => route.fulfill({ json: uploaded ? [{ ...fixture.chapters[0], title: 'TEST FIXTURE: Sections after reupload' }] : [fixture.chapters[0]] }));
  await page.route('**/api/library/books/upload', route => { uploaded = true; return route.fulfill({ json: { document: fixture.book, duplicate: true } }); });
  const card = await openBook(page, fixture.book);
  await expect(card.getByRole('heading', { name: fixture.chapters[0].title, exact: true })).toBeVisible();
  await page.getByLabel('Choose a PDF or EPUB').setInputFiles({ name: 'large.pdf', mimeType: 'application/pdf', buffer: Buffer.from('TEST FIXTURE upload intercepted by Playwright') });
  await expect(card.getByRole('heading', { name: 'TEST FIXTURE: Sections after reupload', exact: true })).toBeVisible();
  await expect(card.getByRole('heading', { name: fixture.chapters[0].title, exact: true })).toHaveCount(0);
});

test('reader navigation keeps chapters and pages separate and retains earlier extraction access', async ({ page }) => {
  const fixture = documentFixture();
  const previous = { ...fixture.chapters[1], id: randomUUID(), itemId: randomUUID(), title: 'TEST FIXTURE: Old extraction', isSuperseded: true };
  await page.route(`**/api/library/books/${fixture.book.id}/sections`, route => route.fulfill({ json: [fixture.pages[0], fixture.chapters[0], previous, fixture.pages[1], fixture.chapters[1]] }));
  for (const section of [fixture.chapters[0], fixture.chapters[1], fixture.pages[0], fixture.pages[1], previous]) {
    const item: Item = { id: section.itemId, documentId: fixture.book.id, sectionId: section.id, kind: 'book-section', title: section.title, canonicalUrl: `local://book/${fixture.book.id}`, provider: 'local-document', body: 'TEST FIXTURE: source text from a private document', textProvenance: 'user-document', ingestedAt: fixture.book.createdAt, topics: [], notes: [], related: [], state: { saved: false, read: false, dismissed: false, understood: false, progress: 0, feedback: 0 } };
    await page.route(`**/api/items/${section.itemId}`, route => route.fulfill({ json: item }));
    await page.route(`**/api/items/${section.itemId}/state`, route => route.fulfill({ json: item }));
  }
  await page.goto(`/items/${fixture.chapters[0].itemId}`);
  const navigation = page.getByRole('navigation', { name: 'Document navigation', exact: true });
  await expect(navigation.getByRole('link', { name: 'Next chapter', exact: true })).toHaveAttribute('href', `/items/${fixture.chapters[1].itemId}`);
  await expect(navigation.getByRole('link', { name: 'Previous chapter', exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: previous.title, exact: true })).not.toBeVisible();
  await page.getByRole('button', { name: 'Contents', exact: true }).click();
  await page.locator('summary').filter({ hasText: /^Previous extraction/ }).click();
  await expect(page.getByRole('link', { name: previous.title, exact: true })).toBeVisible();
  await page.goto(`/items/${fixture.pages[0].itemId}`);
  await expect(navigation.getByRole('link', { name: 'Next page', exact: true })).toHaveAttribute('href', `/items/${fixture.pages[1].itemId}`);
  await expect(page.getByRole('link', { name: 'Next chapter', exact: true })).toHaveCount(0);
  await page.goto(`/items/${previous.itemId}`);
  await expect(page.getByText('This is an earlier version with your saved notes and progress.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Contents', exact: true }).click();
  await expect(page.getByRole('link', { name: previous.title, exact: true })).toBeVisible();
});

test('EPUB navigation respects supplied spine order and page-only PDFs explain the fallback', async ({ page }) => {
  const fixture = documentFixture();
  const book = { ...fixture.book, fileName: 'ordered.epub', pageCount: 0 };
  const chapters = [{ ...fixture.chapters[0], title: 'TEST FIXTURE: Z comes first', locator: 'OEBPS/z.xhtml#start', startPage: null, endPage: null }, { ...fixture.chapters[1], title: 'TEST FIXTURE: A comes second', locator: 'OEBPS/a.xhtml#start', startPage: null, endPage: null }];
  await page.route(`**/api/library/books/${book.id}/sections`, route => route.fulfill({ json: chapters }));
  const card = await openBook(page, book);
  await expect(card.getByRole('heading', { level: 3 }).nth(2)).toHaveText(chapters[0].title);
  await expect(card.getByRole('link', { name: 'Read', exact: true }).first()).toHaveAttribute('href', `/items/${chapters[0].itemId}`);
  await page.route(`**/api/library/books/${fixture.book.id}/sections`, route => route.fulfill({ json: fixture.pages }));
  const pdfCard = await openBook(page, fixture.book);
  await expect(pdfCard.getByText(/No chapters were detected. Read individual pages below/)).toBeVisible();
  await expect(pdfCard.getByRole('heading', { name: 'Page 1', exact: true })).toBeVisible();
  await expect(pdfCard.getByRole('heading', { name: 'Page 1', exact: true }).locator('..').getByText('Page 1', { exact: true })).toHaveCount(2);
});
