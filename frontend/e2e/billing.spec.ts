import { test, expect } from '@playwright/test';
import { login, registerVisit, tenant } from './helpers';

/**
 * M17 billing edge paths through the UI billing panel: a pre-payment discount, capturing
 * the discounted balance, a refund that reopens the receivable, and a credit note that
 * waives it (Adjusted). Mirrors the invariants proven at the API level in the E2E suite.
 */
test.describe.configure({ mode: 'serial' });

test('billing: discount → payment → refund → credit note', async ({ page }) => {
  await login(page, tenant.admin);
  await registerVisit(page); // lands on visit details; the seeded test is priced at 80 EGP

  const billing = page.locator('.card', { hasText: 'Billing (M17)' });
  await expect(billing).toContainText('80');

  // Discount before payment → balance falls to 60.
  await page.locator('#bill-discount').fill('20');
  await page.locator('#bill-discount-reason').fill('Corporate rate');
  await billing.getByRole('button', { name: 'Apply discount' }).click();
  await expect(billing).toContainText('Corporate rate');

  // Capture the discounted 60 in cash → Paid.
  await page.locator('#bill-pay').fill('60');
  await billing.getByRole('button', { name: 'Capture' }).click();
  await expect(billing.getByText('Paid', { exact: true })).toBeVisible();

  // Refund the 60 → the receivable reopens.
  await page.locator('#bill-refund').fill('60');
  await page.locator('#bill-refund-reason').fill('Service complaint');
  await billing.getByRole('button', { name: 'Refund' }).click();
  await expect(billing).toContainText('refunded 60');

  // A credit note waives the reopened balance → Adjusted, settled.
  await page.locator('#bill-credit').fill('60');
  await page.locator('#bill-credit-reason').fill('Goodwill waiver');
  await billing.getByRole('button', { name: 'Issue credit note' }).click();
  await expect(billing.getByText('Adjusted', { exact: true })).toBeVisible();
  await expect(billing).toContainText('Goodwill waiver');
});
