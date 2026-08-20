import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const tenant = JSON.parse(readFileSync(join(__dirname, '.auth', 'tenant.json'), 'utf8')) as {
  tenantId: string; userName: string; password: string;
};
const stamp = Date.now().toString().slice(-6);
const testCode = 'PW' + stamp;
const patientName = 'PW Patient ' + stamp;

test.describe.configure({ mode: 'serial' });

test('front-office happy path through the real UI: login → catalogue → patient → visit', async ({ page }) => {
  // ---------- Login ----------
  await page.goto('/login');
  await page.locator('#tenant').fill(tenant.tenantId);
  await page.locator('#username').fill(tenant.userName);
  await page.locator('#password').fill(tenant.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/dashboard/);

  // ---------- Test Catalogue: create → submit → approve (→ Active) ----------
  await page.goto('/catalog');
  await page.locator('#code').fill(testCode);
  await page.locator('#name').fill('Playwright Glucose');
  await page.locator('#dept').fill('Chemistry');
  await page.locator('#st').selectOption({ label: 'Serum (SST (gold))' });
  await page.locator('#price').fill('90');
  await page.getByRole('button', { name: /Create test/ }).click();

  const row = page.locator('table.t tr', { hasText: testCode });
  await expect(row).toContainText('Draft');
  await row.getByRole('button', { name: /Submit for review/ }).click();
  await expect(row).toContainText('InReview');
  await row.getByRole('button', { name: /Approve/ }).click();
  await expect(row).toContainText('Active');

  // ---------- Register a patient ----------
  await page.goto('/patients');
  await page.locator('#fullName').fill(patientName);
  await page.locator('#dob').fill('1991-02-03');
  await page.locator('#mobile').fill('+2010' + stamp + '00');
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByText(/Patient registered/)).toBeVisible();

  // ---------- Register a visit (the flow the ngSubmit bug broke) ----------
  await page.goto('/visits/new');
  await page.locator('#term').fill(patientName);
  await page.getByRole('button', { name: 'Search' }).click();
  await page.getByRole('button', { name: /use record/ }).click();

  // step 2 — branch is preselected; pick the test we just activated
  await expect(page.getByText('Branch & Tests')).toBeVisible();
  await page.locator('.pick', { hasText: testCode }).locator('input[type="checkbox"]').check();
  await page.getByRole('button', { name: /Confirm visit/ }).click();

  // step 3 — the visit and its invoice exist
  await expect(page.getByText(/Visit .* registered/)).toBeVisible();
  await expect(page.getByText(/Invoice .* issued/)).toBeVisible();
});
