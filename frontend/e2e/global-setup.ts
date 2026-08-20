import { request, type FullConfig } from '@playwright/test';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Provisions a throwaway tenant + admin over the API before the UI test runs, so the
 * spec can log in and exercise the real portal. The admin account is created
 * asynchronously by the TenantProvisioned outbox consumer, so we poll login until it
 * exists. Credentials are handed to the spec via e2e/.auth/tenant.json.
 */
const API = process.env.API_URL || 'http://localhost:5178/api/v1';
const PLATFORM_USER = process.env.PLATFORM_USER || 'platform.admin';
const PLATFORM_PASS = process.env.PLATFORM_PASS || 'SkyLIS#Platform2026!';

export default async function globalSetup(_config: FullConfig): Promise<void> {
  const ctx = await request.newContext();

  const plog = await ctx.post(`${API}/auth/platform-login`, {
    data: { userName: PLATFORM_USER, password: PLATFORM_PASS },
  });
  if (!plog.ok()) throw new Error(`platform-login failed: ${plog.status()} ${await plog.text()}`);
  const ph = { Authorization: `Bearer ${(await plog.json()).token}` };

  const subdomain = 'pw-' + Date.now().toString(36);
  const admin = { userName: 'pwadmin', password: 'Playwright#Lab2026!' };
  const prov = await ctx.post(`${API}/platform/tenants`, {
    headers: ph,
    data: {
      legalName: 'Playwright Lab', subdomain, countryCode: 'EG', planCode: 'PROFESSIONAL',
      isolationTier: 'SharedRls',
      adminUserName: admin.userName, adminFullName: 'PW Admin', adminPassword: admin.password,
    },
  });
  if (!prov.ok()) throw new Error(`provision failed: ${prov.status()} ${await prov.text()}`);
  const tenantId = (await prov.json()).id as string;

  let ready = false;
  for (let i = 0; i < 25; i++) {
    const login = await ctx.post(`${API}/auth/login`, {
      data: { tenantId, userName: admin.userName, password: admin.password },
    });
    if (login.ok()) { ready = true; break; }
    await new Promise(r => setTimeout(r, 1500));
  }
  if (!ready) throw new Error('tenant admin was not created by the outbox consumer in time');

  mkdirSync(join(__dirname, '.auth'), { recursive: true });
  writeFileSync(join(__dirname, '.auth', 'tenant.json'), JSON.stringify({ tenantId, ...admin }));
  await ctx.dispose();
}
