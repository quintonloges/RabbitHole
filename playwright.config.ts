import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e', fullyParallel: false, workers: 1,
  timeout: 60000, expect: { timeout: 15000 },
  use: { baseURL: 'http://127.0.0.1:3001', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    { command: 'dotnet run --project tests/host/RabbitHole.E2eHost.csproj --no-launch-profile', url: 'http://127.0.0.1:5081/api/healthz', timeout: 120000, reuseExistingServer: !process.env.CI },
    { command: 'npm --prefix apps/web run dev -- --port 3001', url: 'http://127.0.0.1:3001', timeout: 120000, reuseExistingServer: !process.env.CI,
      env: { API_ORIGIN: 'http://127.0.0.1:5081', NEXT_TELEMETRY_DISABLED: '1' } }
  ]
});
