import { test, expect } from '@playwright/test';
import { login, logout, registerVisit, collectAndReceive, tenant } from './helpers';

/**
 * P09.5 amendment: a signed, reported result is corrected by a DIFFERENT user (SoD — the
 * enterer can't amend their own result), preserving the old value, and re-released as an
 * AMENDED report.
 */
test.describe.configure({ mode: 'serial' });

test('amend a signed result and re-release it as an AMENDED report', async ({ page }) => {
  page.on('dialog', d => d.accept());

  // ---- Admin drives the visit to a FINAL report ----
  await login(page, tenant.admin);
  const { patientName, visitUrl } = await registerVisit(page);
  await collectAndReceive(page);

  await page.goto('/results');
  const resRow = page.locator('table.t tr', { hasText: patientName });
  await resRow.locator('input').fill('88');
  await resRow.getByRole('button', { name: /Enter/ }).click();
  // Wait for the entry to commit (the pending row clears) before moving on.
  await expect(page.locator('table.t tr', { hasText: patientName })).toHaveCount(0);

  await page.goto('/validation');
  await page.locator('table.t tr', { hasText: patientName }).getByRole('button', { name: 'Accept' }).click();
  await expect(page.getByText(/Technically Valid/)).toBeVisible();

  // ---- Doctor signs out, renders FINAL, then amends (admin was the enterer — SoD holds) ----
  await logout(page);
  await login(page, tenant.doctor);

  await page.goto('/validation');
  await page.getByRole('button', { name: /Medical Sign-Out/ }).click();
  await page.locator('table.t tr', { hasText: patientName }).getByRole('button', { name: /Sign/ }).click();
  await expect(page.getByText(/Medically Valid/)).toBeVisible();

  await page.goto('/reports');
  const repRow = page.locator('table.t tr', { hasText: patientName });
  await repRow.getByRole('button', { name: /Render FINAL/ }).click();
  await expect(repRow).toContainText('Reported');

  // Amend the signed result on the visit page.
  await page.goto(visitUrl);
  const results = page.locator('.card', { hasText: 'Results' });
  // Wait for the results panel to load (it fetches independently of the invoice panel)
  // before reaching for the Amend control.
  await expect(results.getByText('MedicallyValid')).toBeVisible();
  await results.getByRole('button', { name: /Amend/ }).click();
  await page.locator('#amend-value').fill('95');
  await page.locator('#amend-reason').fill('Recalculated after instrument recalibration');
  await page.getByRole('button', { name: 'Apply amendment' }).click();
  await expect(results).toContainText('Amended');
  await expect(results).toContainText('was 88');

  // Re-release as an AMENDED report.
  await page.goto('/reports');
  const amendRow = page.locator('table.t tr', { hasText: patientName });
  await amendRow.getByRole('button', { name: /Render amended/ }).click();
  await expect(page.locator('.note')).toContainText('Amended');
});
