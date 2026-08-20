import { defineConfig, devices } from '@playwright/test';

/**
 * Browser-level smoke test for the Client Portal. Guards the flows that a green
 * API-only E2E suite cannot see — every one runs through real clicks, so a dead
 * (ngSubmit) binding or an un-wired button fails the build instead of shipping silently.
 *
 * Assumes the API and the portal dev server are already running:
 *   API    → http://localhost:5178  (ASPNETCORE_ENVIRONMENT=Development)
 *   portal → http://localhost:4300  (npx ng serve client-portal --port 4300)
 * Override with PORTAL_URL / API_URL (CI points these at the compose stack).
 */
export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  timeout: 45_000,
  expect: { timeout: 12_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.PORTAL_URL || 'http://localhost:4300',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    actionTimeout: 12_000,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
