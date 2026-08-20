import { test, expect } from '@playwright/test';
import { login, logout, registerVisit, collectAndReceive, tenant } from './helpers';

/**
 * P10 interim report: a visit with two tests where only one is signed out is InProcess,
 * so the validated subset can be released as an INTERIM report (a FINAL is refused until
 * every test is medically valid — that gate is covered at the API level).
 */
test.describe.configure({ mode: 'serial' });

test('interim report releases the validated subset of a partly-signed visit', async ({ page }) => {
  page.on('dialog', d => d.accept());

  await login(page, tenant.admin);
  // Two tests consolidate onto one Serum/Fasting specimen.
  const { patientName } = await registerVisit(page, [tenant.seedTestCode, tenant.seedTestCode2]);
  await collectAndReceive(page);

  // Enter a result for the FIRST test only.
  await page.goto('/results');
  const row = page.locator('table.t tr').filter({ hasText: patientName }).filter({ hasText: tenant.seedTestCode });
  await row.locator('input').fill('92');
  await row.getByRole('button', { name: /Enter/ }).click();
  // The second test stays pending; only one entry row remains for this patient.
  await expect(page.locator('table.t tr').filter({ hasText: patientName })).toHaveCount(1);

  // Technical accept (admin), then medical sign-out (doctor — SoD).
  await page.goto('/validation');
  await page.locator('table.t tr', { hasText: patientName }).getByRole('button', { name: 'Accept' }).click();
  await expect(page.getByText(/Technically Valid/)).toBeVisible();

  await logout(page);
  await login(page, tenant.doctor);
  await page.goto('/validation');
  await page.getByRole('button', { name: /Medical Sign-Out/ }).click();
  await page.locator('table.t tr', { hasText: patientName }).getByRole('button', { name: /Sign/ }).click();
  await expect(page.getByText(/Medically Valid/)).toBeVisible();

  // The visit is InProcess (1 of 2 signed) → an INTERIM report is available.
  await page.goto('/reports');
  const repRow = page.locator('table.t tr', { hasText: patientName });
  await expect(repRow).toContainText('1 / 2');
  await repRow.getByRole('button', { name: /Render interim/ }).click();
  await expect(page.locator('.note')).toContainText('Interim');
});
