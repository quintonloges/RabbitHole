import { defineConfig, devices } from '@playwright/test';

// The lab has no API dependency. Its entire suite can run with only the web app.
export default defineConfig({
  testDir: './tests/e2e', testMatch: 'blackjack*.spec.ts', fullyParallel: true, workers: 2,
  timeout: 45_000, expect: { timeout: 10_000 },
  use: { baseURL: 'http://127.0.0.1:3002', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'npm --prefix apps/web run dev -- --port 3002', url: 'http://127.0.0.1:3002/blackjack',
    timeout: 120_000, reuseExistingServer: !process.env.CI,
    env: { NEXT_TELEMETRY_DISABLED: '1' },
  },
});
