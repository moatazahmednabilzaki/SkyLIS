import { test, expect } from '@playwright/test';
import { login, tenant } from './helpers';

/**
 * P17.2 cashier day-close through the UI: open a shift with a float, close it, and read
 * the Z-report (expected vs. declared cash and the variance).
 */
test.describe.configure({ mode: 'serial' });

test('cashier: open shift → close → Z-report reconciles', async ({ page }) => {
  await login(page, tenant.admin);
  await page.goto('/cashier');

  // Repeat-run safety: close any shift left open on this shared tenant.
  if (await page.locator('#declared').count() > 0) {
    await page.locator('#declared').fill('0');
    await page.getByRole('button', { name: /Close shift/ }).click();
    await expect(page.getByText(/Z-Report/)).toBeVisible();
  }

  // Open a fresh shift (MAIN is preselected) with a 200 float.
  await page.locator('#float').fill('200');
  await page.getByRole('button', { name: 'Open shift' }).click();
  // The close form (and its declared-cash field) only exist once a shift is open.
  await expect(page.locator('#declared')).toBeVisible();

  // Close it with the drawer counted at exactly the float → variance 0.
  await page.locator('#declared').fill('200');
  await page.getByRole('button', { name: /Close shift/ }).click();

  // Scope to the card by its heading — the open-shift card's "Close shift — Z-report"
  // button would otherwise also match a plain "Z-Report" substring.
  const zreport = page.locator('.card').filter({ has: page.getByRole('heading', { name: /^Z-Report/ }) });
  await expect(zreport).toBeVisible();
  await expect(zreport).toContainText('Expected cash');
  await expect(zreport).toContainText('Variance');
});
