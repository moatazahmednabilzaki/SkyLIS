import { test, expect } from '@playwright/test';
import { login, registerVisit, tenant } from './helpers';

/**
 * P09.4 critical-value handling through the UI: entering a panic value flags it, the call
 * without read-back keeps it open, and only a documented read-back closes it. (The FINAL
 * report gate on open criticals is covered at the API level in the E2E suite.)
 */
test.describe.configure({ mode: 'serial' });

test('critical value → call documented → read-back closes it', async ({ page }) => {
  await login(page, tenant.admin);
  const { patientName } = await registerVisit(page);

  // Collect + receive the specimen.
  await page.getByRole('button', { name: /Collect/ }).click();
  await expect(page.getByRole('button', { name: 'Receive' })).toBeVisible();
  await page.getByRole('button', { name: 'Receive' }).click();
  await expect(page.getByRole('button', { name: 'Receive' })).toHaveCount(0);

  // Enter a critically low glucose (below the seeded critical-low of 40).
  await page.goto('/results');
  const resRow = page.locator('table.t tr', { hasText: patientName });
  await resRow.locator('input').fill('30');
  await resRow.getByRole('button', { name: /Enter/ }).click();
  await expect(page.locator('table.t tr', { hasText: patientName })).toHaveCount(0);

  // It appears on the critical console, flagged and open.
  await page.goto('/critical');
  const critRow = page.locator('table.t tr', { hasText: patientName });
  await expect(critRow).toContainText('30');

  // A call without read-back keeps it open (ReadBackDocumented).
  await critRow.getByPlaceholder('Who was called').fill('Dr. Hossam Fathy');
  await critRow.getByPlaceholder('Phone').fill('+201224567890');
  await critRow.getByRole('button', { name: 'Document' }).click();
  await expect(page.locator('table.t tr', { hasText: patientName })).toContainText('ReadBackDocumented');

  // Read-back confirmed closes it.
  const openRow = page.locator('table.t tr', { hasText: patientName });
  await openRow.getByPlaceholder('Who was called').fill('Dr. Hossam Fathy');
  await openRow.getByPlaceholder('Phone').fill('+201224567890');
  await openRow.locator('input[type="checkbox"]').check();
  await openRow.getByRole('button', { name: 'Document' }).click();
  await expect(page.getByText(/closed with read-back/i)).toBeVisible();
});
