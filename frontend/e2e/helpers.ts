import { expect, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

export const tenant = JSON.parse(readFileSync(join(__dirname, '.auth', 'tenant.json'), 'utf8')) as {
  tenantId: string;
  admin: { userName: string; password: string };
  doctor: { userName: string; password: string };
  seedTestCode: string;
  seedTestCode2: string;
};

export async function login(page: Page, user: { userName: string; password: string }): Promise<void> {
  await page.goto('/login');
  await page.locator('#tenant').fill(tenant.tenantId);
  await page.locator('#username').fill(user.userName);
  await page.locator('#password').fill(user.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/dashboard/);
}

export async function logout(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login/);
}

let seq = 0;
function uniquePatientName(): string {
  seq += 1;
  return `PW ${Date.now().toString().slice(-6)}-${seq}`;
}

/**
 * Registers a fresh patient and a visit ordering the seeded test, then opens the visit
 * details page. Returns the patient name for scoping later worklist assertions.
 */
export async function registerVisit(
  page: Page,
  testCodes: string[] = [tenant.seedTestCode],
): Promise<{ patientName: string; visitUrl: string }> {
  const patientName = uniquePatientName();
  const mobile = '+20100' + String(2_000_000 + Math.floor(Math.random() * 7_000_000));

  await page.goto('/patients');
  await page.locator('#fullName').fill(patientName);
  await page.locator('#dob').fill('1990-01-01');
  await page.locator('#mobile').fill(mobile);
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByText(/Patient registered/)).toBeVisible();

  await page.goto('/visits/new');
  await page.locator('#term').fill(patientName);
  await page.getByRole('button', { name: 'Search' }).click();
  await page.getByRole('button', { name: /use record/ }).click();
  await expect(page.getByText('Branch & Tests')).toBeVisible();
  for (const code of testCodes) {
    await page.locator('.pick', { hasText: code }).locator('input[type="checkbox"]').check();
  }
  await page.getByRole('button', { name: /Confirm visit/ }).click();
  await expect(page.getByText(/Visit .* registered/)).toBeVisible();

  await page.getByRole('button', { name: /Open visit details/ }).click();
  await expect(page).toHaveURL(/\/visits\//);
  return { patientName, visitUrl: page.url() };
}

/** Collect + receive the single (consolidated) specimen on the visit-details page. */
export async function collectAndReceive(page: Page): Promise<void> {
  await page.getByRole('button', { name: /Collect/ }).click();
  await expect(page.getByRole('button', { name: 'Receive' })).toBeVisible();
  await page.getByRole('button', { name: 'Receive' }).click();
  await expect(page.getByRole('button', { name: 'Receive' })).toHaveCount(0);
}
