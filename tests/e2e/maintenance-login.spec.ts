import { test, expect } from '@playwright/test';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fixturePassword } from './auth-fixture';

test('automated release login uses the real cookie and CSRF endpoints with a private password file', async ({ request }) => {
  const folder = await mkdtemp(join(tmpdir(), 'rabbithole-ci-login-'));
  const passwordFile = join(folder, 'password');
  const command = process.platform === 'win32' ? 'python' : 'python3';
  const args = ['scripts/hosted/smoke.py', '--base', 'http://127.0.0.1:5081/api', '--check-login-only', '--password-file', passwordFile];
  try {
    await writeFile(passwordFile, fixturePassword, { mode: 0o600 });
    const { stdout, stderr } = await promisify(execFile)(command, args);
    expect(stdout).toContain('PASS: existing administrator credentials authenticated');
    expect(stdout + stderr).not.toContain(fixturePassword);
    expect((await request.get('/api/settings')).status()).toBe(401);
    await writeFile(passwordFile, 'TEST-FIXTURE-wrong-password');
    await expect(promisify(execFile)(command, args)).rejects.toMatchObject({ code: 1 });
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});
