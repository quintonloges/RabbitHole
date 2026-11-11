import { randomUUID } from 'node:crypto';
import type { APIRequestContext, Page } from '@playwright/test';
import { test, expect } from './auth-fixture';
import type { Book, BookSection } from '../../apps/web/lib/types';

// Real, deterministic PDF bytes with visibly distinct original layouts. Never production seed data.
function pdfFixture(imageOnly = false) {
  const count = imageOnly ? 1 : 3;
  const objects = ['', '', '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'];
  const kids: string[] = [];
  for (let index = 0; index < count; index++) {
    const pageId = objects.length + 1;
    kids.push(`${pageId} 0 R`);
    const text = (size: number, x: number, y: number, content: string) => `BT /F1 ${size} Tf ${x} ${y} Td (${content}) Tj ET`;
    const stream = [
      '0.98 0.97 0.94 rg 0 0 612 792 re f',
      '0.18 0.25 0.28 rg 52 300 135 115 re f',
      '0.68 0.31 0.22 rg 207 300 135 175 re f',
      '0.42 0.48 0.31 rg 362 300 135 80 re f',
      ...(imageOnly ? [] : [
        '0.12 0.12 0.12 rg', text(11, 52, 740, 'TEST FIXTURE / ORIGINAL DOCUMENT'),
        text(27, 52, 680, ['Following a curious thought', 'A different perspective', 'Returning to the source'][index]),
        text(13, 52, 635, 'This supplied page has its own typography, spacing and illustration.'),
        text(13, 52, 612, 'The viewer preserves the original layout instead of rebuilding text.'),
        text(11, 52, 270, 'Illustrative shapes - no measurements or research claims.'),
        text(12, 52, 210, 'TEST FIXTURE: PDF text remains selectable and the source stays private.'),
        text(11, 52, 52, `Original page ${index + 1}`),
      ]),
    ].join('\n');
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents ${pageId + 1} 0 R >>`);
    objects.push(`<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}\nendstream`);
  }
  objects[0] = '<< /Type /Catalog /Pages 2 0 R >>';
  objects[1] = `<< /Type /Pages /Kids [${kids.join(' ')}] /Count ${count} >>`;
  let pdf = `%PDF-1.4\n% TEST FIXTURE ${randomUUID()}\n`;
  const offsets = [0];
  objects.forEach((object, index) => { offsets.push(Buffer.byteLength(pdf)); pdf += `${index + 1} 0 obj\n${object}\nendobj\n`; });
  const xref = Buffer.byteLength(pdf);
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  pdf += offsets.slice(1).map(offset => `${String(offset).padStart(10, '0')} 00000 n \n`).join('');
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(pdf);
}

async function upload(request: APIRequestContext, imageOnly = false) {
  const buffer = pdfFixture(imageOnly);
  const response = await request.post('/api/library/books/upload', { multipart: { file: { name: `TEST-FIXTURE-viewer-${randomUUID()}.pdf`, mimeType: 'application/pdf', buffer } } });
  expect(response.ok()).toBe(true);
  const data = await response.json() as { document: Book; sections: BookSection[] };
  return { ...data, buffer };
}

async function rendered(page: Page, number: number, total = 3) {
  await expect(page.locator('.pdf-caption').getByRole('status')).toHaveText(`Page ${number} of ${total}`);
  await expect(page.locator('.react-pdf__Page__canvas')).toBeVisible();
}

test('the library opens original PDF pages with local rendering, navigation, zoom and remembered dark pages', async ({ page, request, playwright }) => {
  const data = await upload(request);
  const first = data.sections.find(section => section.startPage === 1)!;
  const externalRequests: string[] = [];
  const errors: string[] = [];
  page.on('request', req => { if (/^https?:/.test(req.url()) && !req.url().startsWith('http://127.0.0.1:3001/')) externalRequests.push(req.url()); });
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/admin/library');
  await page.getByRole('tab', { name: 'Books & files' }).click();
  const card = page.locator('.document-card').filter({ has: page.getByRole('heading', { name: data.document.title, exact: true }) });
  await card.getByRole('button', { name: 'Read PDF', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/admin/items/${first.itemId}$`));
  await rendered(page, 1);
  await expect(page.locator('.reader-body')).toHaveCount(0);
  await expect(page.locator('.react-pdf__Page__textContent')).toContainText('Following a curious thought');
  await expect(page.getByRole('button', { name: 'Previous PDF page', exact: true })).toBeDisabled();
  const dark = page.getByRole('button', { name: 'Dark pages', exact: true });
  if (await dark.getAttribute('aria-pressed') === 'true') await dark.click();
  await page.setViewportSize({ width: 1440, height: 1000 });
  await expect.poll(async () => {
    const width = (await page.locator('.react-pdf__Page__canvas').boundingBox())?.width || 0;
    const available = await page.locator('.pdf-viewport').evaluate(element => element.clientWidth - 24);
    return Math.abs(width - available);
  }).toBeLessThan(2);
  await page.screenshot({ path: 'test-results/pdf-viewer-desktop-original.png', fullPage: true });
  await dark.click();
  await expect(page.locator('.react-pdf__Page__canvas')).toHaveCSS('filter', 'invert(0.9) hue-rotate(180deg)');
  await page.screenshot({ path: 'test-results/pdf-viewer-desktop-dark.png', fullPage: true });
  const canvas = page.locator('.react-pdf__Page__canvas');
  const originalWidth = (await canvas.boundingBox())!.width;
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
  await expect.poll(async () => (await canvas.boundingBox())!.width).toBeGreaterThan(originalWidth);
  await page.getByRole('button', { name: '125%', exact: true }).click();
  await page.getByRole('button', { name: 'Next PDF page', exact: true }).click();
  await rendered(page, 2);
  await expect(page.locator('.reader-entry-scope')).toHaveText('Notes, read status and Extracted text apply to Page 1.');
  await expect(page.getByRole('heading', { name: 'Notes for Page 1', exact: true })).toBeVisible();
  await expect(page.getByText('Reading entry 1 of 3', { exact: true })).toHaveCount(2);
  await expect(page.getByText('No notes yet. Use Extracted text to highlight a passage or quote it in a note.', { exact: true })).toBeVisible();
  await expect(page.locator('.reader-progress > summary')).toHaveText('Progress for Page 1 · 0%');
  await expect(page.locator('.react-pdf__Page__textContent')).toContainText('A different perspective');
  await page.getByLabel('PDF page number', { exact: true }).fill('3');
  await page.getByLabel('PDF page number', { exact: true }).press('Enter');
  await rendered(page, 3);
  await expect(page.getByRole('button', { name: 'Next PDF page', exact: true })).toBeDisabled();
  await page.reload();
  await rendered(page, 3);
  await expect(dark).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Previous PDF page', exact: true }).click();
  await rendered(page, 2);
  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(async () => (await canvas.boundingBox())?.width ?? Infinity).toBeLessThan(390);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: 'test-results/pdf-viewer-mobile-dark.png', fullPage: true });
  const state = await (await request.get(`/api/items/${first.itemId}`)).json();
  expect(state.state.read).toBe(false);
  expect(state.state.understood).toBe(false);
  expect(state.state.progress).toBe(0);
  const file = await request.get(`/api/library/books/${data.document.id}/file`);
  expect(await file.body()).toEqual(data.buffer);
  const range = await request.get(`/api/library/books/${data.document.id}/file`, { headers: { Range: 'bytes=0-4' } });
  expect(range.status()).toBe(206);
  expect(await range.text()).toBe('%PDF-');
  const anonymous = await playwright.request.newContext({ baseURL: 'http://127.0.0.1:3001' });
  expect((await anonymous.get(`/api/library/books/${data.document.id}/file`)).status()).toBe(401);
  await anonymous.dispose();
  expect(externalRequests).toEqual([]);
  expect(errors).toEqual([]);
});

test('section links start at their actual PDF page and extracted text still supports saved notes', async ({ page, request }) => {
  const data = await upload(request);
  const section = data.sections.find(entry => entry.startPage === 2)!;
  await page.goto(`/admin/items/${section.itemId}`);
  await rendered(page, 2);
  await page.getByRole('button', { name: 'Extracted text', exact: true }).click();
  await expect(page.locator('.reader-body')).toContainText('A different perspective');
  await page.getByLabel('Your note').fill('TEST FIXTURE: This note survives switching views.');
  await page.getByRole('button', { name: 'Save note', exact: true }).click();
  await expect(page.locator('.reader-note')).toContainText('This note survives switching views.');
  await page.getByRole('button', { name: 'Original PDF', exact: true }).click();
  await rendered(page, 2);
  await page.reload();
  await rendered(page, 2);
  await expect(page.locator('.reader-note')).toContainText('This note survives switching views.');
});

test('PDFs with no extracted text display their original artwork', async ({ page, request }) => {
  const data = await upload(request, true);
  expect(data.document.status).toBe('original-only');
  await page.goto(`/admin/items/${data.sections[0].itemId}`);
  await rendered(page, 1, 1);
  expect(await page.locator('canvas').evaluate((canvas: HTMLCanvasElement) => {
    const context = canvas.getContext('2d')!;
    const pixel = context.getImageData(Math.floor(canvas.width / 2), Math.floor(canvas.height / 2), 1, 1).data;
    return pixel[0] > pixel[1] * 1.5 && pixel[3] === 255;
  })).toBe(true);
  await page.getByRole('button', { name: 'Extracted text', exact: true }).click();
  await expect(page.locator('.reader-content-notice').last()).toContainText('No text was extracted');
});

test('file failures stay recoverable with text fallback and an explicit retry', async ({ page, request }) => {
  const data = await upload(request);
  const route = `**/api/library/books/${data.document.id}/file`;
  await page.route(route, handler => handler.fulfill({ status: 404, contentType: 'application/problem+json', body: '{"detail":"TEST FIXTURE: missing file"}' }));
  await page.goto(`/admin/items/${data.sections[0].itemId}`);
  await expect(page.locator('.pdf-viewer').getByRole('alert')).toContainText('could not be displayed');
  await expect(page.locator('.pdf-viewer').getByRole('link', { name: 'Open original file', exact: true })).toHaveAttribute('href', `/api/library/books/${data.document.id}/file#page=1`);
  await page.getByRole('button', { name: 'Extracted text', exact: true }).click();
  await expect(page.locator('.reader-body')).toContainText('Following a curious thought');
  await page.getByRole('button', { name: 'Original PDF', exact: true }).click();
  await expect(page.locator('.pdf-viewer').getByRole('alert')).toBeVisible();
  await page.unroute(route);
  await page.getByRole('button', { name: 'Retry PDF', exact: true }).click();
  await rendered(page, 1);
});
