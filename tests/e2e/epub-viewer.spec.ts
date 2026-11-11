import { readFile } from 'node:fs/promises';
import type { APIRequestContext, Page, Request } from '@playwright/test';
import { test, expect } from './auth-fixture';
import type { Book, BookSection } from '../../apps/web/lib/types';

async function upload(request: APIRequestContext) {
  const buffer = await readFile('tests/e2e/fixtures/epub-reader.epub');
  const response = await request.post('/api/library/books/upload', { multipart: { file: { name: 'TEST-FIXTURE-reader.epub', mimeType: 'application/epub+zip', buffer } } });
  expect(response.ok()).toBe(true);
  return { ...await response.json() as { document: Book; sections: BookSection[] }, buffer };
}

async function rendered(page: Page, heading: string) {
  await expect(page.locator('.epub-frame-wrap')).toHaveAttribute('aria-busy', 'false');
  await expect(page.frameLocator('.epub-viewer iframe').getByRole('heading', { name: heading, exact: true })).toBeVisible();
}

test('original EPUB preserves formatting and images with private resources, chapter links, zoom and dark pages', async ({ page, request, playwright }) => {
  const data = await upload(request);
  const first = data.sections[0];
  const externalRequests: Request[] = [];
  const errors: string[] = [];
  page.on('request', req => { if (/^https?:/.test(req.url()) && !req.url().startsWith('http://127.0.0.1:3001/')) externalRequests.push(req); });
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/admin/library');
  await page.getByRole('tab', { name: 'Books & files' }).click();
  const card = page.locator('.document-card').filter({ has: page.getByRole('heading', { name: data.document.title, exact: true }) });
  await card.getByRole('button', { name: 'Read EPUB', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/admin/items/${first.itemId}$`));
  await rendered(page, 'Following a curious thought');
  const frame = page.frameLocator('.epub-viewer iframe');
  await expect(frame.locator('body')).toHaveCSS('background-color', 'rgb(250, 247, 239)');
  const illustration = frame.getByAltText('Three geometric shapes from the original book');
  await expect(illustration).toBeVisible();
  expect(await illustration.evaluate((el: HTMLImageElement) => el.complete && el.naturalWidth > 0)).toBe(true);
  await expect(frame.locator('script, iframe, form, input, meta')).toHaveCount(0);
  await expect(page.locator('.reader-body')).toHaveCount(0);
  await expect(page.locator('.epub-viewer iframe')).toHaveAttribute('sandbox', 'allow-same-origin');
  const dark = page.getByRole('button', { name: 'Dark pages', exact: true });
  if (await dark.getAttribute('aria-pressed') === 'true') await dark.click();
  await page.setViewportSize({ width: 1440, height: 1100 });
  await page.screenshot({ path: 'test-results/epub-desktop-original.png', fullPage: true });
  await dark.click();
  await expect(page.locator('.epub-viewer iframe')).toHaveCSS('filter', 'invert(0.9) hue-rotate(180deg)');
  await page.screenshot({ path: 'test-results/epub-desktop-dark.png', fullPage: true });
  await page.getByRole('button', { name: 'Zoom in EPUB', exact: true }).click();
  await expect(frame.locator('html')).toHaveCSS('zoom', '1.25');
  await page.getByRole('button', { name: '125%', exact: true }).click();
  await frame.getByRole('link', { name: 'Continue to the second chapter' }).click();
  await rendered(page, 'A different perspective');
  await expect(page.getByRole('combobox', { name: 'EPUB chapter', exact: true })).toHaveValue(/two.xhtml(?:#second)?$/);
  await expect(page.locator('.epub-viewer iframe')).toHaveAttribute('src', /two.xhtml#second$/);
  const before = (await (await request.get(`/api/items/${first.itemId}`)).json()).state;
  expect(before.read).toBe(false);
  expect(before.progress).toBe(0);
  await page.reload();
  await rendered(page, 'A different perspective');
  await expect(dark).toHaveAttribute('aria-pressed', 'true');
  const anonymous = await playwright.request.newContext({ baseURL: 'http://127.0.0.1:3001' });
  const href = await page.locator('.epub-viewer iframe').getAttribute('src');
  expect((await anonymous.get(href!)).status()).toBe(401);
  expect((await anonymous.get(`/api/library/books/${data.document.id}/epub`)).status()).toBe(401);
  await anonymous.dispose();
  expect(await (await request.get(`/api/library/books/${data.document.id}/file`)).body()).toEqual(data.buffer);
  // Chromium reports attempted CSS loads before CSP rejects them, with no network response.
  expect(externalRequests.length).toBeGreaterThan(0);
  for (const external of externalRequests) {
    expect(external.failure()?.errorText).toBe('csp');
    expect(await external.response()).toBeNull();
  }
  expect(errors).toEqual([]);
});

test('EPUB starts at the selected chapter, remembers scrolling, and keeps notes tied to the saved entry', async ({ page, request }) => {
  const data = await upload(request);
  const second = data.sections.find(section => section.title === 'A different perspective')!;
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`/admin/items/${second.itemId}`);
  await rendered(page, 'A different perspective');
  await page.screenshot({ path: 'test-results/epub-mobile.png', fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.frameLocator('.epub-viewer iframe').locator('body').evaluate(el => el.ownerDocument.defaultView!.scrollTo(0, 900));
  await expect.poll(() => page.evaluate(id => JSON.parse(localStorage.getItem(`rabbithole-epub-position:${id}`) || '{}').scroll, second.itemId)).toBeGreaterThan(800);
  await page.reload();
  await expect(page.locator('.epub-frame-wrap')).toHaveAttribute('aria-busy', 'false');
  await expect.poll(() => page.locator('.epub-viewer iframe').evaluate((el: HTMLIFrameElement) => el.contentWindow!.scrollY)).toBeGreaterThan(800);
  await expect(page.locator('.reader-entry-scope')).toContainText('A different perspective');
  await page.getByRole('button', { name: 'Extracted text', exact: true }).click();
  await expect(page.locator('.reader-body')).toContainText('Second chapter source passage');
  await page.getByLabel('Your note').fill('TEST FIXTURE: An EPUB note survives switching views.');
  await page.getByRole('button', { name: 'Save note', exact: true }).click();
  await expect(page.locator('.reader-note')).toContainText('An EPUB note survives switching views.');
  await page.locator('summary').filter({ hasText: /^Progress within this/ }).click();
  await page.getByRole('slider', { name: 'Reading progress' }).fill('38');
  await page.getByRole('slider', { name: 'Reading progress' }).blur();
  await expect.poll(async () => (await (await request.get(`/api/items/${second.itemId}`)).json()).state.progress).toBe(.38);
  await page.getByRole('button', { name: 'Original EPUB', exact: true }).click();
  await expect(page.locator('.epub-frame-wrap')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('.reader-progress summary')).toContainText('Progress for A different perspective');
  await page.reload();
  await expect(page.locator('.reader-note')).toContainText('An EPUB note survives switching views.');
});

for (const departure of ['switching views', 'navigating to another entry'] as const) {
  test(`EPUB retains its position after ${departure}`, async ({ page, request }) => {
    const data = await upload(request);
    const second = data.sections.find(section => section.title === 'A different perspective')!;
    const stateBefore = (await (await request.get(`/api/items/${second.itemId}`)).json()).state;
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/admin/items/${second.itemId}`);
    await rendered(page, 'A different perspective');
    const frame = page.locator('.epub-viewer iframe');
    const href = await frame.getAttribute('src');
    const saved = () => page.evaluate(id => JSON.parse(localStorage.getItem(`rabbithole-epub-position:${id}`) || '{}'), second.itemId);
    await frame.evaluate((el: HTMLIFrameElement) => el.contentWindow!.scrollTo(0, 900));
    await expect.poll(saved).toEqual({ href, scroll: 900 });
    const expectedScroll = departure === 'switching views' ? 1200 : 900;

    if (departure === 'switching views') {
      // Leave on the next native scroll event, before its debounced save can run.
      await page.getByRole('button', { name: 'Extracted text', exact: true }).evaluate(button => new Promise<void>(resolve => {
        const view = document.querySelector<HTMLIFrameElement>('.epub-viewer iframe')!.contentWindow!;
        view.addEventListener('scroll', () => { (button as HTMLButtonElement).click(); resolve(); }, { once: true });
        view.scrollTo(0, 1200);
      }));
      await expect(frame).toHaveCount(0);
      await expect.poll(saved).toEqual({ href, scroll: expectedScroll });
      await page.getByRole('button', { name: 'Original EPUB', exact: true }).click();
    } else {
      await page.getByRole('navigation', { name: 'Document navigation', exact: true }).getByRole('link', { name: 'Previous section', exact: true }).click();
      await expect(page).toHaveURL(new RegExp(`/admin/items/${data.sections[0].itemId}$`));
      await rendered(page, 'Following a curious thought');
      await expect.poll(saved).toEqual({ href, scroll: expectedScroll });
      await page.goBack();
    }

    await expect(page.locator('.epub-frame-wrap')).toHaveAttribute('aria-busy', 'false');
    await expect(frame).toHaveAttribute('src', href!);
    await expect.poll(() => frame.evaluate((el: HTMLIFrameElement) => el.contentWindow!.scrollY)).toBe(expectedScroll);
    const item = await (await request.get(`/api/items/${second.itemId}`)).json();
    expect(item.state.read).toBe(stateBefore.read);
    expect(item.state.progress).toBe(stateBefore.progress);
  });
}

test('unavailable EPUB offers retry, original download, and extracted text', async ({ page, request }) => {
  const data = await upload(request);
  const url = `**/api/library/books/${data.document.id}/epub`;
  await page.route(url, route => route.fulfill({ status: 422, contentType: 'application/problem+json', body: JSON.stringify({ detail: 'TEST FIXTURE: This EPUB cannot be displayed.' }) }));
  await page.goto(`/admin/items/${data.sections[0].itemId}`);
  await expect(page.locator('.epub-viewer').getByRole('alert')).toContainText('This EPUB cannot be displayed.');
  await expect(page.getByRole('link', { name: 'Download original EPUB' })).toHaveAttribute('href', `/api/library/books/${data.document.id}/file`);
  await page.getByRole('button', { name: 'Extracted text', exact: true }).click();
  await expect(page.locator('.reader-body')).toContainText('This supplied EPUB');
  await page.getByRole('button', { name: 'Original EPUB', exact: true }).click();
  await expect(page.locator('.epub-viewer').getByRole('alert')).toBeVisible();
  await page.unroute(url);
  await page.getByRole('button', { name: 'Retry EPUB', exact: true }).click();
  await rendered(page, 'Following a curious thought');
});
