import { test, expect, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const t = JSON.parse(readFileSync(join(__dirname, '.auth', 'tenant.json'), 'utf8')) as {
  tenantId: string;
  admin: { userName: string; password: string };
  doctor: { userName: string; password: string };
};
const stamp = Date.now().toString().slice(-6);
const testCode = 'PW' + stamp;
const patientName = 'PW Patient ' + stamp;

async function login(page: Page, user: { userName: string; password: string }): Promise<void> {
  await page.goto('/login');
  await page.locator('#tenant').fill(t.tenantId);
  await page.locator('#username').fill(user.userName);
  await page.locator('#password').fill(user.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/dashboard/);
}

async function logout(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login/);
}

test.describe.configure({ mode: 'serial' });

test('full clinical chain through the real UI', async ({ page }) => {
  // Native confirm() on medical sign-out is auto-accepted.
  page.on('dialog', d => d.accept());

  // ================= Front office (as the tenant admin) =================
  await login(page, t.admin);

  // --- Catalogue: create -> submit -> approve -> result schema ---
  await page.goto('/catalog');
  await page.locator('#code').fill(testCode);
  await page.locator('#name').fill('Playwright Glucose');
  await page.locator('#dept').fill('Chemistry');
  await page.locator('#st').selectOption({ label: 'Serum (SST (gold))' });
  await page.locator('#price').fill('90');
  await page.getByRole('button', { name: /Create test/ }).click();

  const catRow = page.locator('table.t tr', { hasText: testCode });
  await expect(catRow).toContainText('Draft');
  await catRow.getByRole('button', { name: /Submit for review/ }).click();
  await expect(catRow).toContainText('InReview');
  await catRow.getByRole('button', { name: /Approve/ }).click();
  await expect(catRow).toContainText('Active');

  // A result schema is required before results can be entered (M09).
  await catRow.getByRole('button', { name: /result schema/ }).click();
  await page.locator('#sc-unit').fill('mg/dL');
  await page.locator('#sc-refLow').fill('70');
  await page.locator('#sc-refHigh').fill('100');
  await page.getByRole('button', { name: 'Save schema' }).click();
  await expect(catRow).toContainText('✅');

  // --- Patient ---
  await page.goto('/patients');
  await page.locator('#fullName').fill(patientName);
  await page.locator('#dob').fill('1991-02-03');
  await page.locator('#mobile').fill('+2010' + stamp + '00');
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByText(/Patient registered/)).toBeVisible();

  // --- Register the visit ---
  await page.goto('/visits/new');
  await page.locator('#term').fill(patientName);
  await page.getByRole('button', { name: 'Search' }).click();
  await page.getByRole('button', { name: /use record/ }).click();
  await expect(page.getByText('Branch & Tests')).toBeVisible();
  await page.locator('.pick', { hasText: testCode }).locator('input[type="checkbox"]').check();
  await page.getByRole('button', { name: /Confirm visit/ }).click();
  await expect(page.getByText(/Visit .* registered/)).toBeVisible();

  // --- Collect + receive the specimen ---
  await page.getByRole('button', { name: /Open visit details/ }).click();
  await expect(page).toHaveURL(/\/visits\//);
  await page.getByRole('button', { name: /Collect/ }).click();
  await expect(page.getByRole('button', { name: 'Receive' })).toBeVisible();
  await page.getByRole('button', { name: 'Receive' }).click();
  await expect(page.getByRole('button', { name: 'Receive' })).toHaveCount(0);

  // --- Enter the result ---
  await page.goto('/results');
  const resRow = page.locator('table.t tr', { hasText: patientName });
  await resRow.locator('input').fill('92');
  await resRow.getByRole('button', { name: /Enter/ }).click();
  await expect(page.locator('table.t tr', { hasText: patientName })).toHaveCount(0);

  // --- Technical validation (same user may accept; SoD only bars medical) ---
  await page.goto('/validation');
  const techRow = page.locator('table.t tr', { hasText: patientName });
  await techRow.getByRole('button', { name: 'Accept' }).click();
  await expect(page.getByText(/Technically Valid/)).toBeVisible();

  // ================= Medical sign-out (as a DIFFERENT user — SoD) =================
  await logout(page);
  await login(page, t.doctor);

  await page.goto('/validation');
  await page.getByRole('button', { name: /Medical Sign-Out/ }).click();
  const medRow = page.locator('table.t tr', { hasText: patientName });
  await medRow.getByRole('button', { name: /Sign/ }).click();
  await expect(page.getByText(/Medically Valid/)).toBeVisible();

  // --- Render the FINAL report ---
  await page.goto('/reports');
  const repRow = page.locator('table.t tr', { hasText: patientName });
  await repRow.getByRole('button', { name: /Render FINAL/ }).click();
  await expect(repRow).toContainText('Final');
  await expect(repRow).toContainText('Reported');
});
