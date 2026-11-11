import { randomUUID } from 'node:crypto';
import type { APIRequestContext } from '@playwright/test';
import { test, expect } from './auth-fixture';
import type { Item, Source } from '../../apps/web/lib/types';

async function addSource(request: APIRequestContext, name: string, url: string) {
  const response = await request.post('/api/sources', { data: { name, kind: 'rss', url, enabled: false, resultLimit: 100 } });
  expect(response.ok()).toBe(true);
  const source: Source = await response.json();
  const refresh = await request.post(`/api/sources/${source.id}/refresh`);
  expect(refresh.ok()).toBe(true);
  expect((await refresh.json()).status).toBe('completed');
  return source;
}

async function sourceItems(request: APIRequestContext, sourceId: string, offset = 0): Promise<{ items: Item[]; total: number }> {
  const response = await request.get(`/api/items?sourceId=${sourceId}&limit=100&offset=${offset}`);
  expect(response.ok()).toBe(true);
  return response.json();
}

test('anonymous visitors can browse all listed content by source, including shared imports', async ({ request, browser, baseURL }, testInfo) => {
  const key = randomUUID();
  const feed = `https://example.org/source-library/${key}/feed.xml?large=true`;
  const primary = await addSource(request, `TEST FIXTURE: Public & source, ${key.slice(0, 8)}`, feed);
  const secondary = await addSource(request, `TEST FIXTURE: Second public source ${key.slice(0, 8)}`, `${feed}&secondary=true`);
  const { items } = await sourceItems(request, primary.id);
  expect(items).toHaveLength(60);
  await request.patch(`/api/items/${items[0].id}/state`, { data: { saved: true, read: true, dismissed: true } });
  expect((await request.patch(`/api/items/${items[1].id}/publication`, { data: { isPublic: false } })).ok()).toBe(true);
  const context = await browser.newContext({ baseURL, viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  const privateRequests: string[] = [];
  page.on('request', outgoing => {
    const path = new URL(outgoing.url()).pathname;
    if (path.startsWith('/api/') && !path.startsWith('/api/public/')) privateRequests.push(path);
  });
  try {
    await page.goto('/?sort=newest');
    await page.locator('.story-row').getByRole('link', { name: primary.name, exact: true }).first().click();
    await expect(page.getByRole('heading', { name: primary.name, exact: true })).toBeVisible();
    expect(new URL(page.url()).searchParams.get('source')).toBe(primary.name);
    await expect(page.getByText('59 items · Newest added first', { exact: true })).toBeVisible();
    await expect(page.locator('.story-row')).toHaveCount(24);
    await expect(page.locator('select[name="source"]')).toHaveValue(primary.name);
    const listed: string[] = [];
    for (let number = 1; number <= 3; number++) {
      await expect(page.getByText(`Page ${number} of 3`, { exact: true })).toBeVisible();
      listed.push(...await page.locator('.story-explore').evaluateAll(links => links.map(link => link.getAttribute('href')!)));
      if (number < 3) await page.getByRole('link', { name: 'Next', exact: true }).click();
    }
    expect(new Set(listed).size).toBe(59);
    expect(listed.sort()).toEqual(items.filter(item => item.id !== items[1].id).map(item => `/items/${item.id}`).sort());
    await page.reload();
    await expect(page.locator('.story-row')).toHaveCount(11);
    await page.goBack();
    await expect(page.getByText('Page 2 of 3', { exact: true })).toBeVisible();
    expect(new URL(page.url()).searchParams.get('source')).toBe(primary.name);
    await page.goto(`/items/${items[0].id}`);
    await page.locator('.public-item-header').getByRole('link', { name: secondary.name, exact: true }).click();
    await expect(page.getByRole('heading', { name: secondary.name, exact: true })).toBeVisible();
    await expect(page.getByText('59 items · Newest added first', { exact: true })).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath('public-source-desktop.png'), fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.evaluate(() => { document.documentElement.dataset.theme = 'light'; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('public-source-mobile.png'), fullPage: true });
    expect((await request.patch(`/api/items/${items[0].id}/publication`, { data: { isPublic: false } })).ok()).toBe(true);
    await page.reload();
    await expect(page.getByText('58 items · Newest added first', { exact: true })).toBeVisible();
    await expect(page.locator(`.story-explore[href="/items/${items[0].id}"]`)).toHaveCount(0);
    await page.getByRole('link', { name: 'All sources →', exact: true }).click();
    await expect(page).toHaveURL(`${baseURL}/search`);
    expect(privateRequests).toEqual([]);
  } finally { await context.close(); }
});

test('source names open all imported content, retain the filter, and work after a subscription is removed', async ({ page, request }, testInfo) => {
  const key = randomUUID();
  const feed = `https://example.org/source-library/${key}/feed.xml`;
  const primary = await addSource(request, `TEST FIXTURE: Curious sources ${key.slice(0, 8)}`, feed);
  const secondary = await addSource(request, `TEST FIXTURE: Another source ${key.slice(0, 8)}`, `${feed}?secondary=true`);
  const { items } = await sourceItems(request, primary.id);
  expect(items).toHaveLength(3);
  const saved = items[0];
  expect((await request.patch(`/api/items/${saved.id}/state`, { data: { saved: true } })).ok()).toBe(true);
  const unrelated = await (await request.post('/api/items/import-url', { data: { url: `https://example.org/unrelated-${key}` } })).json();
  await request.patch(`/api/items/${unrelated.id}/state`, { data: { saved: true } });

  await page.goto('/admin/settings');
  await page.getByRole('link', { name: primary.name, exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/admin/library\\?sourceId=${primary.id}$`));
  await expect(page.getByRole('heading', { name: primary.name, exact: true })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'All items', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.locator('.collection-row')).toHaveCount(3);
  await expect(page.getByRole('link', { name: saved.title, exact: true })).toBeVisible();
  await expect(page.getByText('3 imported items', { exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('source-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.evaluate(() => { document.documentElement.dataset.theme = 'light'; });
  await expect(page.getByRole('link', { name: 'All sources', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('source-mobile.png'), fullPage: true });

  await page.getByRole('link', { name: saved.title, exact: true }).click();
  await page.locator('.reader-header').getByRole('link', { name: secondary.name, exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`sourceId=${secondary.id}$`));
  await expect(page.locator('.collection-row')).toHaveCount(3);
  await page.goBack();
  await expect(page).toHaveURL(new RegExp(`/admin/items/${saved.id}$`));
  await page.goBack();
  await expect(page).toHaveURL(new RegExp(`sourceId=${primary.id}$`));
  await page.reload();
  await expect(page.locator('.collection-row')).toHaveCount(3);
  await page.getByRole('tab', { name: 'All items', exact: true }).click();
  await expect(page.locator('.collection-row')).toHaveCount(3);
  await page.getByLabel('Filter items').fill(items[1].title);
  await expect(page.locator('.collection-row')).toHaveCount(1);
  await page.getByLabel('Filter items').fill('no matching fixture');
  await expect(page.getByRole('heading', { name: 'No matching items' })).toBeVisible();
  await page.getByLabel('Filter items').fill('');
  await page.getByRole('tab', { name: 'Saved', exact: true }).click();
  await expect(page.locator('.collection-row')).toHaveCount(1);
  await page.getByRole('button', { name: 'Remove from library', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'No saved items from this source' })).toBeVisible();
  await page.getByRole('button', { name: 'Browse all items', exact: true }).click();
  await expect(page.locator('.collection-row')).toHaveCount(3);
  await request.patch(`/api/items/${saved.id}/state`, { data: { saved: true } });
  expect((await request.delete(`/api/sources/${primary.id}`)).ok()).toBe(true);
  await page.reload();
  await expect(page.getByRole('heading', { name: primary.name, exact: true })).toBeVisible();
  await expect(page.locator('.collection-row')).toHaveCount(3);
  await page.getByRole('link', { name: secondary.name, exact: true }).first().click();
  await expect(page.getByRole('heading', { name: secondary.name, exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'All sources', exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/library$/);
  await expect(page.locator(`.collection-row h3 a[href="/admin/items/${unrelated.id}"]`)).toBeVisible();
});

test('source pagination includes every saved item and retries failures without losing the source', async ({ page, request }) => {
  test.setTimeout(120000);
  const key = randomUUID();
  const source = await addSource(request, `TEST FIXTURE: Large source ${key.slice(0, 8)}`, `https://example.org/source-library/${key}-first/feed.xml?large=true`);
  expect((await request.patch(`/api/sources/${source.id}`, { data: { url: `https://example.org/source-library/${key}-second/feed.xml?large=true` } })).ok()).toBe(true);
  expect((await (await request.post(`/api/sources/${source.id}/refresh`)).json()).status).toBe('completed');
  const first = await sourceItems(request, source.id);
  const second = await sourceItems(request, source.id, 100);
  expect(first.total).toBe(120);
  const savedItems = [...first.items, ...second.items].slice(0, 103);
  for (const item of savedItems) {
    expect((await request.patch(`/api/items/${item.id}/state`, { data: { saved: true } })).ok()).toBe(true);
  }
  await page.route('**/api/items?**', async route => {
    if (new URL(route.request().url()).searchParams.get('offset') === '100') {
      await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ detail: 'TEST FIXTURE: temporarily unavailable' }) });
    } else await route.continue();
  });
  await page.goto(`/admin/library?sourceId=${source.id}`);
  await expect(page.getByText('120 imported items', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'Saved', exact: true }).click();
  await expect(page.locator('.collection-row')).toHaveCount(100);
  await expect(page.getByText('103 saved items', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Load more', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'temporarily unavailable' })).toBeVisible();
  await expect(page.locator('.collection-row')).toHaveCount(100);
  await page.unroute('**/api/items?**');
  await page.getByRole('button', { name: 'Load more', exact: true }).click();
  await expect(page.locator('.collection-row')).toHaveCount(103);
  await expect(page.getByRole('button', { name: 'Load more', exact: true })).toHaveCount(0);
  const links = await page.locator('.collection-row h3 a').evaluateAll(elements => elements.map(element => element.getAttribute('href')));
  expect(new Set(links).size).toBe(103);
  expect(links.sort()).toEqual(savedItems.map(item => `/admin/items/${item.id}`).sort());

  // An old page must not append into the first page reloaded after a state edit.
  await page.reload();
  await page.getByRole('tab', { name: 'Saved', exact: true }).click();
  await expect(page.locator('.collection-row')).toHaveCount(100);
  let release!: () => void;
  let captured!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  const fetched = new Promise<void>(resolve => { captured = resolve; });
  await page.route('**/api/items?**', async route => {
    if (new URL(route.request().url()).searchParams.get('offset') === '100') {
      const response = await route.fetch();
      captured();
      await held;
      await route.fulfill({ response });
    } else await route.continue();
  });
  try {
    await page.getByRole('button', { name: 'Load more', exact: true }).click();
    await fetched;
    const row = page.locator('.collection-row').filter({ has: page.getByRole('link', { name: savedItems[0].title, exact: true }) });
    await row.getByRole('button', { name: 'Remove from library', exact: true }).click();
    await expect(page.getByText('102 saved items', { exact: true })).toBeVisible();
    release();
    await page.unrouteAll({ behavior: 'wait' });
    await expect(page.locator('.collection-row')).toHaveCount(100);
    await page.getByRole('button', { name: 'Load more', exact: true }).click();
    await expect(page.locator('.collection-row')).toHaveCount(102);
    const remaining = await page.locator('.collection-row h3 a').evaluateAll(elements => elements.map(element => element.getAttribute('href')));
    expect(new Set(remaining).size).toBe(102);
    expect(remaining.sort()).toEqual(savedItems.slice(1).map(item => `/admin/items/${item.id}`).sort());
  } finally {
    release();
    await page.unrouteAll({ behavior: 'wait' });
  }
  expect((await request.patch(`/api/items/${savedItems[1].id}/publication`, { data: { isPublic: false } })).ok()).toBe(true);
  await page.getByRole('combobox', { name: 'Visibility', exact: true }).selectOption('private');
  await expect(page.locator('.collection-row')).toHaveCount(1);
  await expect(page.getByRole('link', { name: savedItems[1].title, exact: true })).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`sourceId=${source.id}$`));
});
