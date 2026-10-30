import { randomUUID } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test as publicTest } from '@playwright/test';
import { test, expect, fixturePassword } from './auth-fixture';
import type { Item } from '../../apps/web/lib/types';

publicTest('invalid public search retains inputs, explains validation and recovers on correction', async ({ browser, baseURL }) => {
  const context = await browser.newContext({ baseURL }); const page = await context.newPage();
  const content = page.getByRole('main');
  const tooLong = 'x'.repeat(201);
  await page.goto(`/search?q=${tooLong}&source=TEST-FIXTURE-source&type=article`);
  await expect(content.getByRole('alert')).toContainText('Search is limited to 200 characters.');
  await expect(page.getByLabel('Search', { exact: true })).toHaveValue(tooLong);
  await expect(page.getByLabel('Search', { exact: true })).toHaveAttribute('maxlength', '200');
  await expect(page.getByRole('combobox', { name: 'Source', exact: true })).toHaveValue('TEST-FIXTURE-source');
  await expect(page.getByRole('combobox', { name: 'Type', exact: true })).toHaveValue('article');
  await page.getByLabel('Search', { exact: true }).fill('TEST FIXTURE');
  await page.getByLabel('Published from').fill('2026-10-30');
  await page.getByLabel('Published through').fill('2026-10-01');
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await expect(content.getByRole('alert')).toContainText('from must not follow to.');
  await expect(page.getByLabel('Search', { exact: true })).toHaveValue('TEST FIXTURE');
  await expect(page.getByLabel('Published from')).toHaveValue('2026-10-30');
  await expect(page.getByLabel('Published through')).toHaveValue('2026-10-01');
  await expect(page.getByText('The catalog is temporarily unavailable')).toHaveCount(0);
  await page.screenshot({ path: '.cache/ui-review/catalog-validation-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: '.cache/ui-review/catalog-validation-mobile.png', fullPage: true });
  await page.getByLabel('Published through').fill('2026-10-31');
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Public catalog' })).toBeVisible();
  await expect(content.getByRole('alert')).toHaveCount(0);
  await context.close();
});

test('anonymous API access and workspace rendering enforce the private boundary', async ({ playwright, browser, baseURL }) => {
  const anonymous = await playwright.request.newContext({ baseURL });
  for (const path of ['/api/items', '/api/topics', '/api/settings', '/api/holes', '/api/library/books', '/api/practice/prompts', '/api/export', '/api/ai/status', '/api/system', '/api/jobs', '/openapi/v1.json']) {
    const response = await anonymous.get(path);
    expect([401, 404], path).toContain(response.status());
    expect(await response.text(), path).not.toMatch(/TEST NOTE|Private book|passwordHash|connectionString/i);
  }
  expect((await anonymous.get('/api/auth/session')).status()).toBe(401);
  expect((await anonymous.post('/api/auth/login', { data: { password: fixturePassword } })).status()).toBe(400);
  const context = await browser.newContext({ baseURL }); const page = await context.newPage();
  const privateCalls: string[] = [];
  page.on('request', request => { if (/\/api\/(settings|items|holes|topics|library|practice)/.test(request.url())) privateCalls.push(request.url()); });
  await page.goto('/admin/library');
  await expect(page).toHaveURL(/\/admin\/login$/);
  await expect(page.getByRole('heading', { name: 'Private workspace' })).toBeVisible();
  await expect(page.locator('.nav-list')).toHaveCount(0);
  expect(privateCalls).toEqual([]);
  expect((await anonymous.get('/admin/login')).headers()['x-robots-tag']).toContain('noindex');
  await context.close(); await anonymous.dispose();
});

test('password-only sign in, CSRF-backed writes, and logout invalidate access', async ({ browser, baseURL }) => {
  const context = await browser.newContext({ baseURL }); const page = await context.newPage();
  await page.goto('/admin/login');
  await page.getByLabel('Administrator password', { exact: true }).fill('TEST-FIXTURE-wrong-password!');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await page.getByLabel('Administrator password', { exact: true }).fill(fixturePassword);
  const login = page.waitForRequest(request => request.method() === 'POST' && request.url().endsWith('/api/auth/login'));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  expect((await login).headers()['x-csrf-token']).toBeTruthy();
  await expect(page).toHaveURL(/\/admin$/);
  await expect(page.getByRole('heading', { name: 'Discover', exact: true })).toBeVisible();
  const session = await context.request.get('/api/auth/session'); expect(session.ok()).toBe(true);
  expect((await context.request.patch('/api/settings', { data: { editionSize: 6 } })).status()).toBe(400);
  const logout = page.waitForRequest(request => request.method() === 'POST' && request.url().endsWith('/api/auth/logout'));
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  expect((await logout).headers()['x-csrf-token']).toBeTruthy();
  await expect(page).toHaveURL(/\/admin\/login$/);
  expect((await context.request.get('/api/auth/session')).status()).toBe(401);
  expect((await context.request.get('/api/library/books')).status()).toBe(401);
  await page.goto('/admin/settings'); await expect(page).toHaveURL(/\/admin\/login$/);
  await context.close();
});

test('public SSR, filters, related items and sitemap exclude hidden records and personal state', async ({ request, playwright, browser, baseURL }) => {
  const key = randomUUID(); const title = `TEST FIXTURE public catalog ${key}`;
  const sentinel = `PRIVATE-NOTE-${key}`; const hiddenTitle = `HIDDEN-CATALOG-${key}`;
  async function create(label: string) {
    const imported = await request.post('/api/items/import-url', { data: { url: `https://example.org/public-catalog-${randomUUID()}` } });
    expect(imported.ok()).toBe(true); const item = await imported.json() as Item;
    expect(item.isPublic).toBe(true);
    const updated = await request.patch(`/api/items/${item.id}`, { data: { title: label } }); expect(updated.ok()).toBe(true);
    return await updated.json() as Item;
  }
  const item = await create(title); const hidden = await create(hiddenTitle);
  expect((await request.patch(`/api/items/${hidden.id}/publication`, { data: { isPublic: false } })).ok()).toBe(true);
  expect((await request.post(`/api/items/${item.id}/notes`, { data: { body: sentinel } })).ok()).toBe(true);
  expect((await request.patch(`/api/items/${item.id}/state`, { data: { saved: true, dismissed: true, understood: true, progress: .37 } })).ok()).toBe(true);
  const before = await (await request.get(`/api/items/${item.id}`)).json();
  const anonymous = await playwright.request.newContext({ baseURL });
  const search = await anonymous.get(`/api/public/items?q=${encodeURIComponent(title)}`); expect(search.ok()).toBe(true);
  const catalog = await search.json(); expect(catalog.total).toBe(1); expect(catalog.items[0].id).toBe(item.id);
  const publicDetail = await (await anonymous.get(`/api/public/items/${item.id}`)).json();
  expect(Object.keys(publicDetail).sort()).toEqual(['author', 'canonicalUrl', 'embedUrl', 'excerpt', 'id', 'imageUrl', 'ingestedAt', 'provider', 'publishedAt', 'sourceName', 'title', 'topics', 'type'].sort());
  const htmlResponse = await anonymous.get(`/items/${item.id}`); expect(htmlResponse.status()).toBe(200);
  const html = await htmlResponse.text(); expect(html).toContain(title); expect(html).toContain('Original source');
  expect(html).not.toContain(sentinel); expect(html).not.toContain(hiddenTitle); expect(html).not.toMatch(/"saved":true|"progress":0\.37|metadataJson|providerId/);
  expect((await anonymous.get(`/api/public/items?q=${sentinel}`)).ok()).toBe(true);
  expect((await (await anonymous.get(`/api/public/items?q=${sentinel}`)).json()).total).toBe(0);
  expect((await anonymous.get(`/items/${hidden.id}`)).status()).toBe(404);
  expect(await (await anonymous.get(`/items/${hidden.id}`)).text()).not.toContain(hiddenTitle);
  const related = await (await anonymous.get(`/api/public/items/${item.id}/related`)).json();
  expect(related.every((record: { id: string }) => record.id !== hidden.id)).toBe(true);
  const sitemap = await (await anonymous.get('/sitemap.xml')).text(); expect(sitemap).toContain(item.id); expect(sitemap).not.toContain(hidden.id); expect(sitemap).not.toMatch(/\/admin|\/search/);
  const filtered = await anonymous.get(`/?q=${encodeURIComponent(title)}`);
  expect(await filtered.text()).toMatch(/name="robots" content="noindex, follow"/);
  const context = await browser.newContext({ baseURL }); const page = await context.newPage(); const calls: string[] = [];
  page.on('request', request => { if (new URL(request.url()).pathname.startsWith('/api/')) calls.push(request.url()); });
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Discover', exact: true })).toBeVisible();
  await expect(page.locator('main form')).toHaveCount(0);
  await expect(page.getByRole('link', { name: title, exact: true })).toHaveAttribute('href', item.canonicalUrl);
  await page.screenshot({ path: ".cache/ui-review/public-catalog-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: ".cache/ui-review/public-catalog-mobile.png", fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(`/?q=${encodeURIComponent(title)}`);
  await expect(page).toHaveURL(/\/search\?/);
  await expect(page.getByLabel('Search', { exact: true })).toHaveValue(title);
  await expect(page.getByRole('link', { name: title, exact: true })).toHaveAttribute('href', item.canonicalUrl);
  await page.screenshot({ path: '.cache/ui-review/public-search-desktop.png', fullPage: true });
  await context.route(item.canonicalUrl, route => route.fulfill({ contentType: 'text/html', body: '<p>TEST FIXTURE ORIGINAL SOURCE</p>' }));
  const sourceOpened = page.waitForEvent('popup');
  await page.getByRole('link', { name: title, exact: true }).click();
  const sourcePage = await sourceOpened;
  await expect(sourcePage).toHaveURL(item.canonicalUrl);
  await expect(sourcePage.getByText('TEST FIXTURE ORIGINAL SOURCE')).toBeVisible();
  await sourcePage.close();
  await page.getByRole('link', { name: 'Go down the rabbit hole ↘', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/items/${item.id}$`));
  await expect(page.getByRole('heading', { name: title, exact: true, level: 1 })).toBeVisible();
  await expect(page.getByRole('button', { name: /Save|read|rabbit hole|Notes/ })).toHaveCount(0);
  expect(calls).toEqual([]);
  await page.screenshot({ path: ".cache/ui-review/public-item-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: '.cache/ui-review/public-item-mobile.png', fullPage: true });
  const after = await (await request.get(`/api/items/${item.id}`)).json();
  expect(after.state).toEqual(before.state); expect(after.notes).toEqual(before.notes);
  await context.close(); await anonymous.dispose();
});

test('admin publication controls remove pages immediately and documents remain private', async ({ page, request, playwright, baseURL }) => {
  const imported = await request.post('/api/items/import-url', { data: { url: `https://example.org/publication-${randomUUID()}` } });
  const item = await imported.json() as Item;
  const anonymous = await playwright.request.newContext({ baseURL });
  await page.goto(`/admin/items/${item.id}`);
  const visibility = page.getByLabel('Show publicly', { exact: false }); await expect(visibility).toBeChecked();
  await visibility.uncheck(); await expect(visibility).toBeEnabled();
  await expect.poll(async () => (await anonymous.get(`/items/${item.id}`)).status()).toBe(404);
  expect(await (await anonymous.get('/sitemap.xml')).text()).not.toContain(item.id);
  await visibility.check(); await expect(visibility).toBeEnabled();
  await expect.poll(async () => (await anonymous.get(`/items/${item.id}`)).status()).toBe(200);
  await page.goto('/admin/library'); await page.getByRole('tab', { name: 'All items', exact: true }).click();
  await page.getByRole('combobox', { name: 'Visibility', exact: true }).selectOption('public');
  await expect(page.getByLabel('Show publicly').first()).toBeChecked();
  const uploaded = await request.post('/api/library/books/upload', { multipart: { file: { name: 'test-book.epub', mimeType: 'application/epub+zip', buffer: await readFile('tests/e2e/fixtures/test-book.epub') } } });
  expect(uploaded.ok()).toBe(true); const upload = await uploaded.json();
  const bookId = upload.document?.id || upload.id || upload.book?.id; expect(bookId).toBeTruthy();
  const sections = await (await request.get(`/api/library/books/${bookId}/sections`)).json();
  expect(sections.length).toBeGreaterThan(0);
  for (const section of sections) {
    expect((await anonymous.get(`/api/public/items/${section.itemId}`)).status()).toBe(404);
    expect((await anonymous.get(`/items/${section.itemId}`)).status()).toBe(404);
    const rejected = await request.patch(`/api/items/${section.itemId}/publication`, { data: { isPublic: true } }); expect(rejected.status()).toBe(400);
  }
  expect((await anonymous.get(`/api/library/books/${bookId}/file`, { headers: { Range: 'bytes=0-99' } })).status()).toBe(401);
  expect(await (await anonymous.get('/sitemap.xml')).text()).not.toContain(bookId);
  await anonymous.dispose();
});

test('public media requires a deliberate click and never sends personal activity', async ({ request, browser, baseURL }) => {
  const imported = await request.post('/api/items/import-url', { data: { url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ' } });
  expect(imported.ok()).toBe(true); const item = await imported.json() as Item;
  const context = await browser.newContext({ baseURL }); const page = await context.newPage();
  let playerRequests = 0; const calls: string[] = [];
  await page.route('https://www.youtube-nocookie.com/**', route => { playerRequests++; return route.fulfill({ contentType: 'text/html', body: '<p>TEST FIXTURE PLAYER</p>' }); });
  page.on('request', request => { if (new URL(request.url()).pathname.startsWith('/api/')) calls.push(request.url()); });
  const before = (await (await request.get(`/api/items/${item.id}`)).json()).state;
  await page.goto(`/items/${item.id}`); expect(playerRequests).toBe(0); await expect(page.locator('iframe')).toHaveCount(0);
  await page.getByRole('button', { name: 'Load player' }).click();
  await expect(page.locator('iframe')).toHaveAttribute('src', /youtube-nocookie.com\/embed\//);
  await expect.poll(() => playerRequests).toBe(1); expect(calls).toEqual([]);
  expect((await (await request.get(`/api/items/${item.id}`)).json()).state).toEqual(before);
  await context.close();
});

test('public pagination and metadata filters keep a stable newest-added collection', async ({ request, playwright, browser, baseURL }) => {
  const prefix = `TEST FIXTURE paging ${randomUUID()}`;
  const topics = await (await request.get('/api/topics')).json(); const topic = topics[0];
  const ids: string[] = [];
  for (let index = 1; index <= 26; index++) {
    const response = await request.post('/api/items/import-url', { data: { url: `https://example.org/paging-${randomUUID()}` } }); expect(response.ok()).toBe(true);
    const item = await response.json(); ids.push(item.id);
    expect((await request.patch(`/api/items/${item.id}`, { data: { title: `${prefix} source ${index}`, topicIds: [topic.id] } })).ok()).toBe(true);
  }
  const anonymous = await playwright.request.newContext({ baseURL });
  const first = await (await anonymous.get(`/api/public/items?q=${encodeURIComponent(prefix)}&page=1&pageSize=24&topicId=${topic.id}&source=manual&type=article`)).json();
  expect(first.total).toBe(26); expect(first.items).toHaveLength(24); expect(first.items[0].id).toBe(ids.at(-1));
  const second = await (await anonymous.get(`/api/public/items?q=${encodeURIComponent(prefix)}&page=2&pageSize=24`)).json();
  expect(second.items).toHaveLength(2); expect(second.items.map((item: { id: string }) => item.id)).toEqual(ids.slice(0, 2).reverse());
  expect((await (await anonymous.get(`/api/public/items?q=${encodeURIComponent(prefix)}&from=2100-01-01`)).json()).total).toBe(0);
  const context = await browser.newContext({ baseURL }); const page = await context.newPage();
  await page.goto(`/search?q=${encodeURIComponent(prefix)}`); await expect(page.locator('.story-row')).toHaveCount(24);
  await page.getByRole('link', { name: 'Next', exact: true }).click(); await expect(page.locator('.story-row')).toHaveCount(2);
  await expect(page).toHaveURL(/page=2/); await page.reload(); await expect(page.locator('.story-row')).toHaveCount(2);
  await page.getByText('Filter by topic, source, type or date', { exact: true }).click();
  await page.getByRole('combobox', { name: 'Topic', exact: true }).selectOption(topic.id); await page.getByRole('combobox', { name: 'Source', exact: true }).selectOption('manual'); await page.getByRole('combobox', { name: 'Type', exact: true }).selectOption('article');
  await page.getByRole('button', { name: 'Search', exact: true }).click(); await expect(page.locator('.story-row')).toHaveCount(24);
  await expect(page.getByText('26 items · Newest added first', { exact: true })).toBeVisible();
  await page.locator('.story-row').first().getByRole('link', { name: topic.name, exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/\\?topicId=${topic.id}$`));
  await expect(page.getByRole('heading', { name: topic.name, exact: true })).toBeVisible();
  await expect(page.locator('main form')).toHaveCount(0);
  await page.getByRole('link', { name: 'Next', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`topicId=${topic.id}.*page=2`));
  await page.getByRole('link', { name: 'All topics →', exact: true }).click();
  await expect(page).toHaveURL(`${baseURL}/`);
  await context.close(); await anonymous.dispose();
});
