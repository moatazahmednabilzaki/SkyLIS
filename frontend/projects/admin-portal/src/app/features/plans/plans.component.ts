import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/auth.service';

interface Plan {
  id: string;
  code: string;
  name: string;
  monthlyPrice: number;
  currency: string;
  maxUsers: number;
  maxBranches: number;
  monthlyReportQuota: number;
  isActive: boolean;
}

/**
 * P01.3 Plan Builder: subscription plans with entitlements. Quotas are enforced at the
 * point of consumption (user seats, active branches); the report quota is metered
 * (FR-SYS-011) and surfaced on the usage explorer.
 */
@Component({
  selector: 'app-plans',
  imports: [ReactiveFormsModule],
  template: `
    <h1 class="pt">Plans</h1>
    <p class="sub">M01 · P01.3 — subscription plans and entitlements (§8). Egypt canonical plans ship seeded.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note">{{ info() }}</div> }

    <div class="card">
      <table class="t">
        <tr><th>Code</th><th>Name</th><th>Monthly price</th><th>User seats</th><th>Active branches</th><th>Report quota / mo</th></tr>
        @for (p of plans(); track p.id) {
          <tr>
            <td class="mono"><b>{{ p.code }}</b></td>
            <td>{{ p.name }}</td>
            <td class="mono">{{ p.monthlyPrice }} {{ p.currency }}</td>
            <td class="mono">{{ p.maxUsers }}</td>
            <td class="mono">{{ p.maxBranches }}</td>
            <td class="mono">{{ p.monthlyReportQuota }}</td>
          </tr>
        }
      </table>
      <p class="hint">Seats = Active + Locked accounts. Finalized reports are never blocked — overage shows on the usage explorer.</p>
    </div>

    <div class="card">
      <h3>Create / update a plan</h3>
      <form (ngSubmit)="upsert()">
        <div class="f-row">
          <div class="f" style="flex:0 0 150px"><label for="code">CODE</label>
            <input id="code" class="mono" [formControl]="code" placeholder="PROFESSIONAL"></div>
          <div class="f" style="flex:2"><label for="name">NAME</label>
            <input id="name" [formControl]="name"></div>
          <div class="f"><label for="price">MONTHLY PRICE (EGP)</label>
            <input id="price" type="number" step="0.01" [formControl]="monthlyPrice"></div>
        </div>
        <div class="f-row">
          <div class="f"><label for="mu">USER SEATS</label>
            <input id="mu" type="number" [formControl]="maxUsers"></div>
          <div class="f"><label for="mb">ACTIVE BRANCHES</label>
            <input id="mb" type="number" [formControl]="maxBranches"></div>
          <div class="f"><label for="mq">REPORT QUOTA / MONTH</label>
            <input id="mq" type="number" [formControl]="monthlyReportQuota"></div>
          <div class="f" style="flex:0 0 auto; align-self:flex-end">
            <button class="btn" type="submit" [disabled]="code.invalid || name.invalid || busy()">Save plan</button>
          </div>
        </div>
      </form>
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: #fff; margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class PlansComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  readonly plans = signal<Plan[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);

  readonly code = this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(40)]);
  readonly name = this.fb.nonNullable.control('', Validators.required);
  readonly monthlyPrice = this.fb.nonNullable.control(0, Validators.min(0));
  readonly maxUsers = this.fb.nonNullable.control(5, Validators.min(1));
  readonly maxBranches = this.fb.nonNullable.control(1, Validators.min(1));
  readonly monthlyReportQuota = this.fb.nonNullable.control(1000, Validators.min(1));

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      this.plans.set(await firstValueFrom(this.http.get<Plan[]>(`${API_BASE_URL}/platform/plans`)));
    } catch {
      this.error.set('Could not load plans. Is the API running?');
    }
  }

  async upsert(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.put(`${API_BASE_URL}/platform/plans`, {
        code: this.code.value, name: this.name.value,
        monthlyPrice: this.monthlyPrice.value, currency: 'EGP',
        maxUsers: this.maxUsers.value, maxBranches: this.maxBranches.value,
        monthlyReportQuota: this.monthlyReportQuota.value,
      }));
      this.info.set(`Plan ${this.code.value.toUpperCase()} saved ✓`);
      await this.load();
    } catch {
      this.error.set('Saving the plan failed.');
    } finally {
      this.busy.set(false);
    }
  }
}
