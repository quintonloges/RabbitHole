import { test as base, expect, type APIRequestContext } from '@playwright/test';

export const fixturePassword = 'TEST-FIXTURE-password-2026!';
export async function loginApi(request: APIRequestContext) {
  const bootstrap = await request.get('/api/auth/csrf');
  expect(bootstrap.ok()).toBe(true);
  const { token } = await bootstrap.json();
  const login = await request.post('/api/auth/login', { data: { password: fixturePassword }, headers: { 'X-CSRF-TOKEN': token } });
  expect(login.ok()).toBe(true);
  const refreshed = await request.get('/api/auth/csrf');
  expect(refreshed.ok()).toBe(true);
  return (await refreshed.json()).token as string;
}

export const test = base.extend<{ adminSession: void }>({
  request: async ({ playwright, baseURL }, use) => {
    const bootstrap = await playwright.request.newContext({ baseURL });
    const token = await loginApi(bootstrap);
    const request = await playwright.request.newContext({ baseURL, storageState: await bootstrap.storageState(), extraHTTPHeaders: { 'X-CSRF-TOKEN': token } });
    await bootstrap.dispose();
    await use(request);
    await request.dispose();
  },
  adminSession: [async ({ context, request }, use) => {
    await context.addCookies((await request.storageState()).cookies);
    await use();
  }, { auto: true }],
});
export { expect } from '@playwright/test';
