import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import type { Item, Prompt } from '../../apps/web/lib/types';

const createdPrompts: string[] = [];
test.afterEach(async ({ request }) => {
  // Leave no due prompts for another journey using the same fixture host.
  for (const id of createdPrompts.splice(0)) {
    await request.post(`/api/practice/prompts/${id}/attempts`, { data: { response: '', rating: 'skip' } });
  }
});

async function importItem(request: APIRequestContext, label: string): Promise<Item> {
  const key = randomUUID();
  const response = await request.post('/api/items/import-url', {
    data: { url: `https://example.org/state-regression-${key}`, title: `TEST FIXTURE: ${label} ${key}` },
  });
  expect(response.ok()).toBe(true);
  return response.json();
}

async function practicePair(request: APIRequestContext) {
  await request.patch('/api/settings', { data: { practiceEnabled: true } });
  const item = await importItem(request, 'practice state');
  const prompts: Prompt[] = [];
  for (const kind of ['reflection', 'recall']) {
    const response = await request.post('/api/practice/prompts', { data: { itemId: item.id, kind } });
    expect(response.ok()).toBe(true);
    const prompt = await response.json();
    prompts.push(prompt);
    createdPrompts.push(prompt.id);
  }
  return { item, first: prompts[0], second: prompts[1] };
}

function promptCard(page: Page, prompt: Prompt) {
  return page.locator('.panel.panel-pad').filter({ has: page.getByRole('heading', { name: prompt.question, exact: true }) });
}

function gate() {
  let release!: () => void;
  const promise = new Promise<void>(resolve => { release = resolve; });
  return { promise, release };
}

test('a saved row reconciles after PATCH commits, without a stale pre-commit reload', async ({ page, request }) => {
  const item = await importItem(request, 'saved removal');
  await request.patch(`/api/items/${item.id}/state`, { data: { saved: true } });
  await page.goto('/library');
  const row = page.locator('.row-item').filter({ has: page.getByRole('link', { name: item.title, exact: true }) });
  await expect(row.getByRole('button', { name: 'Remove from library', exact: true })).toBeVisible();
  const commit = gate();
  await page.route(`**/api/items/${item.id}/state`, async route => { await commit.promise; await route.continue(); });
  const started = page.waitForRequest(r => r.method() === 'PATCH' && r.url().endsWith(`/items/${item.id}/state`));
  let prematureReloads = 0;
  page.on('request', r => { if (r.method() === 'GET' && r.url().includes('/api/items?saved=true')) prematureReloads++; });
  await row.getByRole('button', { name: 'Remove from library', exact: true }).click();
  await started;
  try {
    await expect(row.getByRole('button', { name: 'Save to library', exact: true })).toBeDisabled();
    await expect(row.getByRole('button', { name: 'Mark read', exact: true })).toBeDisabled();
    expect((await (await request.get(`/api/items/${item.id}`)).json()).state.saved).toBe(true);
    expect(prematureReloads).toBe(0);
  } finally { commit.release(); }
  await expect(row).toHaveCount(0);
  expect((await (await request.get(`/api/items/${item.id}`)).json()).state.saved).toBe(false);
  await page.reload();
  await expect(page.getByRole('link', { name: item.title, exact: true })).toHaveCount(0);
});

test('a failed saved-state mutation restores the row and keeps its error visible', async ({ page, request }) => {
  const item = await importItem(request, 'saved failure');
  await request.patch(`/api/items/${item.id}/state`, { data: { saved: true } });
  await page.goto('/library');
  const row = page.locator('.row-item').filter({ has: page.getByRole('link', { name: item.title, exact: true }) });
  await page.route(`**/api/items/${item.id}/state`, route => route.fulfill({ status: 503, json: { detail: 'TEST FIXTURE: state update failed' } }), { times: 1 });
  await row.getByRole('button', { name: 'Remove from library', exact: true }).click();
  await expect(row.getByRole('alert')).toHaveText('TEST FIXTURE: state update failed');
  await expect(row.getByRole('button', { name: 'Remove from library', exact: true })).toBeEnabled();
  expect((await (await request.get(`/api/items/${item.id}`)).json()).state.saved).toBe(true);
  await row.getByRole('button', { name: 'Remove from library', exact: true }).click();
  await expect(row).toHaveCount(0);
});

test('marking a read-only history item unread reconciles both library caches', async ({ page, request }) => {
  const item = await importItem(request, 'read-only history');
  await request.patch(`/api/items/${item.id}/state`, { data: { read: true } });
  await page.goto('/library');
  await page.getByRole('tab', { name: 'Reading history', exact: true }).click();
  const row = page.locator('.row-item').filter({ has: page.getByRole('link', { name: item.title, exact: true }) });
  await row.getByRole('button', { name: 'Mark unread', exact: true }).click();
  await expect(row).toHaveCount(0);
  expect((await (await request.get(`/api/items/${item.id}`)).json()).state.read).toBe(false);
});

test('snoozing another prompt submits an empty response and preserves the active draft', async ({ page, request }) => {
  const { first, second } = await practicePair(request);
  await page.goto('/practice');
  const firstCard = promptCard(page, first);
  const secondCard = promptCard(page, second);
  await firstCard.getByRole('button', { name: 'Reflect', exact: true }).click();
  await firstCard.getByLabel('What comes to mind?').fill('TEST FIXTURE: first prompt draft only');
  await secondCard.getByRole('button', { name: 'Reflect', exact: true }).click();
  await expect(secondCard.getByLabel('What comes to mind?')).toHaveValue('');
  await secondCard.getByLabel('What comes to mind?').fill('TEST FIXTURE: second prompt draft only');
  await firstCard.getByRole('button', { name: 'Reflect', exact: true }).click();
  await expect(firstCard.getByLabel('What comes to mind?')).toHaveValue('TEST FIXTURE: first prompt draft only');
  const saved = page.waitForResponse(r => r.url().endsWith(`/practice/prompts/${second.id}/attempts`) && r.request().method() === 'POST');
  await secondCard.getByRole('button', { name: 'Not now', exact: true }).click();
  expect((await (await saved).json()).attempts[0].response).toBe('');
  await expect(secondCard).toHaveCount(0);
  await expect(firstCard.getByLabel('What comes to mind?')).toHaveValue('TEST FIXTURE: first prompt draft only');
  const prompts = await (await request.get('/api/practice/prompts')).json() as Prompt[];
  expect(prompts.find(p => p.id === first.id)?.attempts).toHaveLength(0);
});

test('failed reflection preserves its draft and a later save leaves another active draft intact', async ({ page, request }) => {
  const { first, second } = await practicePair(request);
  await page.goto('/practice');
  const firstCard = promptCard(page, first);
  const secondCard = promptCard(page, second);
  await firstCard.getByRole('button', { name: 'Reflect', exact: true }).click();
  await firstCard.getByLabel('What comes to mind?').fill('TEST FIXTURE: retry this answer');
  const endpoint = `**/api/practice/prompts/${first.id}/attempts`;
  await page.route(endpoint, route => route.fulfill({ status: 503, json: { detail: 'TEST FIXTURE: reflection failed' } }), { times: 1 });
  await firstCard.getByRole('button', { name: 'Good', exact: true }).click();
  await expect(page.locator('.error[role="alert"]')).toContainText('TEST FIXTURE: reflection failed');
  await expect(firstCard.getByLabel('What comes to mind?')).toHaveValue('TEST FIXTURE: retry this answer');
  const commit = gate();
  await page.route(endpoint, async route => { await commit.promise; await route.continue(); });
  const started = page.waitForRequest(r => r.url().endsWith(`/practice/prompts/${first.id}/attempts`) && r.method() === 'POST');
  await firstCard.getByRole('button', { name: 'Good', exact: true }).click();
  await started;
  try {
    await secondCard.getByRole('button', { name: 'Reflect', exact: true }).click();
    await secondCard.getByLabel('What comes to mind?').fill('TEST FIXTURE: keep second answer');
  } finally { commit.release(); }
  await expect(firstCard).toHaveCount(0);
  await expect(secondCard.getByLabel('What comes to mind?')).toHaveValue('TEST FIXTURE: keep second answer');
  const prompts = await (await request.get('/api/practice/prompts')).json() as Prompt[];
  expect(prompts.find(p => p.id === first.id)?.attempts?.[0].response).toBe('TEST FIXTURE: retry this answer');
});

test('late AI critique stays with its prompt and is discarded when that draft changes', async ({ page, request }) => {
  const { item, first, second } = await practicePair(request);
  await page.route('**/api/ai/status', route => route.fulfill({ json: { configured: true, enabled: true } }));
  const reply = gate();
  await page.route(`**/api/items/${item.id}/ai`, async route => {
    await reply.promise;
    await route.fulfill({ json: { text: 'TEST FIXTURE: critique of first draft', evidenceItemIds: [item.id] } });
  });
  await page.goto('/practice');
  const firstCard = promptCard(page, first);
  const secondCard = promptCard(page, second);
  await firstCard.getByRole('button', { name: 'Reflect', exact: true }).click();
  await firstCard.getByLabel('What comes to mind?').fill('TEST FIXTURE: first answer');
  const started = page.waitForRequest(r => r.url().endsWith(`/items/${item.id}/ai`));
  await firstCard.getByRole('button', { name: 'Ask AI to compare with source', exact: true }).click();
  await started;
  const finished = page.waitForResponse(r => r.url().endsWith(`/items/${item.id}/ai`));
  try {
    await secondCard.getByRole('button', { name: 'Reflect', exact: true }).click();
    await secondCard.getByLabel('What comes to mind?').fill('TEST FIXTURE: second answer');
  } finally { reply.release(); }
  await (await finished).finished();
  await expect(secondCard.getByRole('button', { name: 'Ask AI to compare with source', exact: true })).toBeEnabled();
  await expect(secondCard).not.toContainText('TEST FIXTURE: critique of first draft');
  await firstCard.getByRole('button', { name: 'Reflect', exact: true }).click();
  await expect(firstCard).toContainText('TEST FIXTURE: critique of first draft');
  const stale = gate();
  await page.route(`**/api/items/${item.id}/ai`, async route => {
    await stale.promise;
    await route.fulfill({ json: { text: 'TEST FIXTURE: obsolete critique', evidenceItemIds: [item.id] } });
  });
  const restarted = page.waitForRequest(r => r.url().endsWith(`/items/${item.id}/ai`));
  await firstCard.getByRole('button', { name: 'Ask AI to compare with source', exact: true }).click();
  await restarted;
  const staleFinished = page.waitForResponse(r => r.url().endsWith(`/items/${item.id}/ai`));
  try { await firstCard.getByLabel('What comes to mind?').fill('TEST FIXTURE: revised first answer'); }
  finally { stale.release(); }
  await (await staleFinished).finished();
  await expect(firstCard.getByRole('button', { name: 'Ask AI to compare with source', exact: true })).toBeEnabled();
  await expect(firstCard).not.toContainText('TEST FIXTURE: obsolete critique');
  await expect(firstCard).not.toContainText('TEST FIXTURE: critique of first draft');
});

test('document extraction retry shows failures and refreshes recovered sections', async ({ page }) => {
  const id = randomUUID();
  const itemId = randomUUID();
  const book = { id, title: `TEST FIXTURE: recovery ${id}`, fileName: 'recovery.pdf', status: 'failed', error: 'TEST FIXTURE: interrupted extraction', pageCount: 1, createdAt: new Date().toISOString() };
  let recovered = false;
  await page.route('**/api/library/books', route => route.fulfill({ json: [{ ...book, ...(recovered ? { status: 'ready', error: null } : {}) }] }));
  await page.route(`**/api/library/books/${id}/sections`, route => route.fulfill({ json: [] }));
  await page.goto('/library');
  await page.getByRole('tab', { name: 'Books & files', exact: true }).click();
  const card = page.locator('.panel.panel-pad').filter({ has: page.getByRole('heading', { name: book.title, exact: true }) });
  await card.getByRole('button', { name: 'Sections', exact: true }).click();
  await expect(card.getByRole('link', { name: 'Open original file', exact: true })).toHaveAttribute('href', `/api/library/books/${id}/file`);
  const endpoint = `**/api/library/books/${id}/retry`;
  await page.route(endpoint, route => route.fulfill({ status: 409, json: { detail: 'TEST FIXTURE: restore the original file before retrying' } }), { times: 1 });
  await card.getByRole('button', { name: 'Retry extraction', exact: true }).click();
  await expect(page.locator('.error[role="alert"]')).toContainText('TEST FIXTURE: restore the original file before retrying');
  const extraction = gate();
  await page.route(endpoint, async route => {
    await extraction.promise;
    recovered = true;
    await route.fulfill({ json: { document: { ...book, status: 'ready', error: null }, sections: [{ id: randomUUID(), documentId: id, itemId, title: 'TEST FIXTURE: recovered chapter', order: 0, startPage: 1, endPage: 1 }] } });
  });
  await card.getByRole('button', { name: 'Retry extraction', exact: true }).click();
  try { await expect(card.getByRole('button', { name: 'Retrying extraction…', exact: true })).toBeDisabled(); }
  finally { extraction.release(); }
  await expect(card.getByRole('heading', { name: 'TEST FIXTURE: recovered chapter', exact: true })).toBeVisible();
  await expect(card.getByRole('link', { name: 'Read', exact: true })).toHaveAttribute('href', `/items/${itemId}`);
  await expect(card.getByRole('button', { name: 'Retry extraction', exact: true })).toHaveCount(0);
  await expect(page.locator('.error[role="alert"]')).toHaveCount(0);
});
