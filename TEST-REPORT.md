# Sky LIS — Phase 1 Full Test Report

**Date:** 2026-08-17 · **Commit:** `864cc5b` (main, pushed) · **Verdict: ALL GREEN — 244 automated checks, 0 failures**

| Layer | Suite | Result |
|---|---|---|
| Domain unit tests | SkyLIS.Domain.Tests | **79 / 79 passed** |
| Application unit tests | SkyLIS.Application.Tests | **11 / 11 passed** |
| Architecture gates | SkyLIS.Architecture.Tests (NetArchTest) | **8 / 8 passed** |
| End-to-end (live API + PostgreSQL 17) | `scripts/e2e.ps1` | **146 / 146 passed** |
| Client Portal | `ng build --configuration production` | **build clean** |
| Admin Portal | `ng build --configuration production` | **build clean** |
| GitHub Actions CI (commit 864cc5b) | Backend · Portals · E2E-with-Postgres | **3 / 3 jobs success** |

The E2E suite is a true black-box run: it provisions two fresh tenants over HTTP against the live API and a real PostgreSQL cluster with Row-Level Security enforced, then walks every Phase 1 workflow including the failure paths (46 of the 146 checks deliberately expect an HTTP error).

---

## 1. Unit & architecture tests (98)

**Domain (79)** — pure business-rule tests, no infrastructure: tenant lifecycle state machine; visit registration, specimen consolidation/reservation, sample state machine, rejection/recollection, cancellation; result evaluation (absurd/reference/critical/delta flags), auto-verification, SoD on medical validation, amendments; report gates (critical value, full validation); invoice payments, discounts, credit notes, refunds, currency guard; cashier shift close/variance; branch & department rules; country-pack content rules; panels; money value object.

**Application (11)** — handler tests over in-memory fakes: visit registration (branch numbering, unknown patient/test/branch, deactivated branch), permission behavior (grant, deny, platform scope).

**Architecture (8)** — enforced by NetArchTest on every build: Domain references no framework; Application never references Infrastructure/API; handlers stay internal; no generic repository; controllers hold no business logic; naming and layering conventions.

## 2. End-to-end suite — 146 checks by module

**M01 Admin Portal (platform)** — provision 2 tenants, duplicate subdomain 409, canonical Egypt plans ship with platform, unknown plan 404, EG country pack present, tenant lifecycle Trial→Active→Suspended→Resumed (suspension blocks sign-in), plan change to LITE, **seat & branch quotas enforced (§8)**, read-only tenant-user monitor (P01.5), master data pack: platform CBC pushed to ALL tenants via outbox, tenant-local activation with price gate (P01.7 / FR-MDM-071), metering counts finalized reports against the plan quota (FR-SYS-011).

**M03 Setup (tenant)** — MAIN branch + EG sample taxonomy auto-seeded on provisioning (FR-TEN-040), departments with dedupe, second branch, sample types with condition trees (P03.4), test create→submit→approve (P03.3), result schemas, **panels/profiles with bundle pricing (P03.5)**, tenant settings: report footer + rejection-reason vocabulary (FR-SYS-004), catalog CSV export/import (FR-SYS-009), setup-wizard checklist green (P03.1).

**M04 Patients** — register, search with identity triple (last visit/age/gender), **Patient 360** (P04.3), **duplicate detection & merge console** — merge re-points visits/results/reports, duplicate vanishes (P04.4), **GDPR data-subject requests** — erasure blocked while clinical work open, anonymization of an empty record, audited data export (P04.5).

**M05/M07/M08 Visits & samples** — registration with consolidation + reservation and per-branch numbering (V-MAIN-…-0001), branch mandatory, reserved-sample time window enforced, collect/receive, rejection → recollection with test rebinding, mandatory patient-information step, reception & phlebotomist worklists, **add-on tests with supplementary invoice** (P05.4), add-on blocked after report, visit cancellation credits *all* invoices.

**M09 Results** — absurd guard, entry blocked before sample receipt, auto-verification, technical queue, SoD (enterer ≠ medical validator ≠ amender), e-signed medical sign-out, critical value: flag → call without read-back stays open → read-back closes (P09.4), rerun voids and reopens, **amendment with mandatory reason + re-signature; old value preserved** (P09.5).

**M10 Reports** — INTERIM for partial validation, FINAL gated on full validation *and* no open critical, one FINAL per visit, byte-stable SHA-256 artifact, delivery log, anonymous public verification (initials only, no PHI; tamper detected), **AMENDED version requires an existing FINAL and renders with the AMENDED marking**, cumulative trend view incl. amended points (P10.3), tenant footer on the artifact.

**M17 Billing** — partial/final payments, overpayment blocked, discount before payment only, refunds (SoD permission) reopen the balance, credit notes close it as Adjusted, automatic credit note on cancellation, **cashier shift & Z-report: expected cash = float + cash in − refunds, variance 0** (P17.2).

**M23 Analytics** — executive dashboard KPIs reconcile exactly with the day's activity (visits, reported, reserved, criticals, revenue net of refunds, median TAT, pipeline), plus TAT/financial/quality detail pages (P23.2–P23.4).

**M02 Users & auth** — initial Tenant Admin created via outbox, real login with roles, wrong password indistinguishable 403, role gates (Technologist blocked from user management and medical validation), lock/unlock (lock blocks sign-in, self-lock blocked), self-service password change (old password dies) — §4.3.

**Cross-cutting platform proofs** — transactional outbox drains with zero poison; attachments round-trip byte-exact with size cap (FR-SYS-007); global search across visits/patients/samples/invoices (FR-SYS-008); **audit hash chain verifies intact and a superuser UPDATE of history is detected** (FR-SYS-001); **tenant isolation: tenant B sees none of tenant A's visits, patients, attachments, or search results (RLS + query filters), and a tenant token cannot reach platform endpoints**.

## 3. How to reproduce

```powershell
# 1. PostgreSQL (local cluster, port 5433)
& 'C:\Program Files\PostgreSQL\17\bin\pg_ctl.exe' start -D D:\PostgreSQL17\data

# 2. Build + unit/architecture tests
dotnet build SkyLIS.sln
dotnet test SkyLIS.sln --no-build

# 3. Database + RLS
dotnet ef database update --project src/SkyLIS.Infrastructure --startup-project src/SkyLIS.Api
psql -U postgres -p 5433 -d skylis -f src/SkyLIS.Infrastructure/Persistence/Scripts/enable-rls.sql

# 4. API (Development, port 5178) — then the E2E suite
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5178'
dotnet run --project src/SkyLIS.Api --no-build
powershell -File scripts/e2e.ps1
```

CI runs the identical sequence from scratch on every push (`.github/workflows/ci.yml`, postgres:17 service container).

## 4. Known simplifications (documented, by design for Phase 1)

- Report artifact is self-contained HTML; PDF is a renderer-port swap (QuestPDF pending a licensing decision).
- Dev JWT issuance stands in for the OpenIddict authority (endpoint mapped in Development only).
- Integration events dispatch in-process through the transactional outbox; MassTransit/RabbitMQ is the Phase 2 transport swap.
- Notifications use the logging dev sender; WhatsApp/SMS/e-mail providers plug into `INotificationSender`.
- Analytics query the OLTP store directly; a projection store arrives with the messaging swap.
