import { test, expect } from '@playwright/test';
import { login, registerVisit, tenant } from './helpers';

/**
 * P07.3 rejection cycle through the UI: collect a specimen, reject it with a coded reason
 * (a recollection spawns automatically), confirm reception is forced to inform the patient,
 * and confirm the recollection lands on the phlebotomist's collection queue.
 */
test.describe.configure({ mode: 'serial' });

test('sample rejection → recollection → mandatory patient information', async ({ page }) => {
  // The rejection reason is captured through a window.prompt.
  page.on('dialog', d => d.accept(d.type() === 'prompt' ? 'HEMOLYZED' : undefined));

  await login(page, tenant.admin);
  const { patientName } = await registerVisit(page);

  // Collect, then reject the specimen.
  await page.getByRole('button', { name: /Collect/ }).click();
  await expect(page.getByRole('button', { name: /Reject/ })).toBeVisible();
  await page.getByRole('button', { name: /Reject/ }).click();
  await expect(page.getByText(/recollection .* issued/i)).toBeVisible();

  // Reception must inform the patient (P07.3 mandatory step).
  await page.goto('/reception');
  await page.getByRole('button', { name: /Patient Information/ }).click();
  const infoRow = page.locator('table.t tr', { hasText: patientName });
  await expect(infoRow).toContainText('HEMOLYZED');
  await infoRow.getByRole('button', { name: /Mark patient informed/ }).click();
  await expect(page.locator('.note')).toContainText('recorded in the audit trail');

  // The recollection is now queued for the phlebotomist.
  await page.goto('/phlebotomist');
  const collectRow = page.locator('table.t tr', { hasText: patientName });
  await expect(collectRow).toContainText('Recollection');
});
