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

  let adminToken = '';
  for (let i = 0; i < 25; i++) {
    const login = await ctx.post(`${API}/auth/login`, {
      data: { tenantId, userName: admin.userName, password: admin.password },
    });
    if (login.ok()) { adminToken = (await login.json()).token; break; }
    await new Promise(r => setTimeout(r, 1500));
  }
  if (!adminToken) throw new Error('tenant admin was not created by the outbox consumer in time');

  // A second user is required for the clinical chain: segregation of duties means the
  // person who enters a result can never medically validate it. LabDirector can sign out
  // results and render reports.
  const doctor = { userName: 'pwdoctor', password: 'Playwright#Doc2026!' };
  const ah = { Authorization: `Bearer ${adminToken}` };
  const createDoc = await ctx.post(`${API}/users`, {
    headers: ah,
    data: { userName: doctor.userName, fullName: 'PW Director', password: doctor.password, roles: ['LabDirector'] },
  });
  if (!createDoc.ok()) throw new Error(`create doctor failed: ${createDoc.status()} ${await createDoc.text()}`);

  // Seed one ready-to-order test (Active, with a critical-capable result schema) so the
  // rejection and critical-value specs don't each have to re-drive the catalogue UI —
  // that path is already covered by the clinical-chain spec. Sample types arrive via the
  // same outbox event as the admin, so poll until the country-pack taxonomy is present.
  let serum: { id: string; conditions: { id: string; name: string }[] } | undefined;
  for (let i = 0; i < 20; i++) {
    const types = await (await ctx.get(`${API}/catalog/sample-types`, { headers: ah })).json();
    serum = (types as typeof serum[]).find(s => (s as { name: string }).name === 'Serum') as typeof serum;
    if (serum) break;
    await new Promise(r => setTimeout(r, 1500));
  }
  if (!serum) throw new Error('country-pack sample types were not seeded in time');
  const condition = serum.conditions.find(c => /Fasting/.test(c.name)) ?? serum.conditions[0];

  const seedTestCode = 'SEED' + Date.now().toString().slice(-5);
  const made = await ctx.post(`${API}/catalog/tests`, {
    headers: ah,
    data: {
      code: seedTestCode, name: 'Seed Glucose', department: 'Chemistry',
      sampleTypeId: serum.id, requiredConditionId: condition.id, price: 80, currency: 'EGP',
    },
  });
  if (!made.ok()) throw new Error(`seed test create failed: ${made.status()} ${await made.text()}`);
  const testId = (await made.json()).id as string;
  await ctx.post(`${API}/catalog/tests/${testId}/submit`, { headers: ah });
  await ctx.post(`${API}/catalog/tests/${testId}/approve`, { headers: ah });
  await ctx.put(`${API}/catalog/tests/${testId}/result-schema`, {
    headers: ah,
    data: {
      unit: 'mg/dL', refLow: 70, refHigh: 100, criticalLow: 40, criticalHigh: 400,
      absurdLow: 5, absurdHigh: 1500, autoVerify: false, deltaThresholdPercent: null,
    },
  });

  mkdirSync(join(__dirname, '.auth'), { recursive: true });
  writeFileSync(join(__dirname, '.auth', 'tenant.json'), JSON.stringify({ tenantId, admin, doctor, seedTestCode }));
  await ctx.dispose();
}
