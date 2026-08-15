import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/auth.service';

interface TenantDto {
  id: string;
  legalName: string;
  subdomain: string;
  countryCode: string;
  planCode: string;
  status: string;
  createdAtUtc: string;
}

interface ProblemDetails {
  detail?: string;
  title?: string;
  errors?: Record<string, string[]>;
}

/** P01.1 Tenant Directory + P01.2 provisioning (wizard steps 3-5 arrive with seeding). */
@Component({
  selector: 'app-tenants',
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    <h1 class="pt">Tenant Directory</h1>
    <p class="sub">M01 · P01.1 — FR-TEN-001. No tenant PHI without a support-access grant.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }

    <div class="card">
      <div class="f-row">
        <div class="f" style="flex:2">
          <label for="search">SEARCH</label>
          <input id="search" [formControl]="search" placeholder="Name or subdomain…" (keyup.enter)="load()">
        </div>
        <div class="f" style="flex:0 0 auto; align-self:flex-end">
          <button class="btn" (click)="load()" [disabled]="loading()">
            {{ loading() ? 'Loading…' : 'Refresh' }}
          </button>
        </div>
      </div>
      @if (tenants().length === 0 && !loading()) {
        <div class="note">No tenants yet — provision the first one below.</div>
      } @else {
        <table class="t">
          <tr><th>Tenant</th><th>Subdomain</th><th>Country</th><th>Plan</th><th>Status</th><th>Created</th><th>Id</th></tr>
          @for (t of tenants(); track t.id) {
            <tr>
              <td><b style="color:#fff">{{ t.legalName }}</b></td>
              <td class="mono">{{ t.subdomain }}.skylis.app</td>
              <td>{{ t.countryCode }}</td>
              <td><span class="chip c-blue">{{ t.planCode }}</span></td>
              <td><span class="chip"
                    [class.c-green]="t.status === 'Active'"
                    [class.c-blue]="t.status === 'Trial'"
                    [class.c-amber]="t.status === 'PastDue'"
                    [class.c-red]="t.status === 'Suspended' || t.status === 'Offboarded'">{{ t.status }}</span></td>
              <td>{{ t.createdAtUtc | date: 'yyyy-MM-dd' }}</td>
              <td class="mono" style="font-size:10px">{{ t.id }}</td>
            </tr>
          }
        </table>
        <p class="hint">Use a tenant id to sign in to the Client Portal (http://localhost:4200).</p>
      }
    </div>

    <div class="card">
      <h3>🚀 Provision tenant (P01.2 — FR-TEN-010)</h3>
      @if (provisioned()) {
        <div class="note">Tenant provisioned ✓ — id <span class="mono">{{ provisioned() }}</span></div>
      }
      <form [formGroup]="form" (ngSubmit)="provision()">
        <div class="f-row">
          <div class="f" style="flex:2">
            <label for="legalName">LEGAL NAME</label>
            <input id="legalName" formControlName="legalName">
          </div>
          <div class="f">
            <label for="subdomain">SUBDOMAIN</label>
            <input id="subdomain" class="mono" formControlName="subdomain" placeholder="lowercase-and-digits">
          </div>
        </div>
        <div class="f-row">
          <div class="f">
            <label for="country">COUNTRY (LOADS DEFAULT PACK — FR-TEN-040)</label>
            <select id="country" formControlName="countryCode">
              <option value="EG">🇪🇬 Egypt</option>
              <option value="SA">🇸🇦 Saudi Arabia</option>
              <option value="AE">🇦🇪 UAE</option>
            </select>
          </div>
          <div class="f">
            <label for="plan">PLAN</label>
            <select id="plan" formControlName="planCode">
              <option value="LITE">LITE</option>
              <option value="STARTER">STARTER</option>
              <option value="PROFESSIONAL">PROFESSIONAL</option>
              <option value="ENTERPRISE">ENTERPRISE</option>
            </select>
          </div>
          <div class="f">
            <label for="tier">ISOLATION TIER</label>
            <select id="tier" formControlName="isolationTier">
              <option value="SharedRls">Shared (RLS)</option>
              <option value="DedicatedSchema">Dedicated schema</option>
              <option value="DedicatedDatabase">Dedicated database</option>
            </select>
          </div>
        </div>
        <button class="btn" type="submit" [disabled]="form.invalid || busy()">
          {{ busy() ? 'Provisioning…' : 'Provision tenant' }}
        </button>
      </form>
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: #fff; margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class TenantsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  readonly tenants = signal<TenantDto[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly provisioned = signal<string | null>(null);

  readonly search = this.fb.nonNullable.control('');
  readonly form = this.fb.nonNullable.group({
    legalName: ['', [Validators.required, Validators.maxLength(200)]],
    subdomain: ['', [Validators.required, Validators.pattern(/^[a-z0-9-]{3,40}$/)]],
    countryCode: ['EG', Validators.required],
    planCode: ['PROFESSIONAL', Validators.required],
    isolationTier: ['SharedRls', Validators.required],
  });

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const params: Record<string, string> = this.search.value ? { search: this.search.value } : {};
      this.tenants.set(await firstValueFrom(
        this.http.get<TenantDto[]>(`${API_BASE_URL}/platform/tenants`, { params })));
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.loading.set(false);
    }
  }

  async provision(): Promise<void> {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    this.provisioned.set(null);
    try {
      const { id } = await firstValueFrom(this.http.post<{ id: string }>(
        `${API_BASE_URL}/platform/tenants`, this.form.getRawValue()));
      this.provisioned.set(id);
      this.form.reset({ countryCode: 'EG', planCode: 'PROFESSIONAL', isolationTier: 'SharedRls' });
      await this.load();
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.busy.set(false);
    }
  }

  private message(error: unknown): string {
    const problem = (error as { error?: ProblemDetails })?.error;
    if (problem?.errors) return Object.values(problem.errors).flat().join(' ');
    return problem?.detail ?? problem?.title ?? 'The request failed. Is the API running with PostgreSQL?';
  }
}
