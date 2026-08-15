import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { OrgApi } from '../org/org.api';
import { Branch, problemMessage } from '../../core/api.types';

interface Shift {
  id: string;
  branchId: string;
  branchCode: string;
  status: string;
  openingFloat: number;
  currency: string;
  openedAtUtc: string;
  closedAtUtc: string | null;
  declaredCash: number | null;
  expectedCash: number | null;
  variance: number | null;
}

interface MethodTotal {
  method: string;
  captured: number;
  refunded: number;
}

interface ZReport {
  shift: Shift;
  byMethod: MethodTotal[];
  cashIn: number;
  cashOut: number;
  expectedCash: number;
  declaredCash: number;
  variance: number;
}

/**
 * P17.2 Cashier & Day Close: one open shift per branch; closing reconciles the counted
 * drawer against expected cash (float + cash in − refunds) and records the variance.
 */
@Component({
  selector: 'app-cashier',
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    <h1 class="pt">Cashier &amp; Day Close</h1>
    <p class="sub">M17 · P17.2 — shift reconciliation (Z-report) per branch.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }

    @if (openShift(); as s) {
      <div class="card">
        <h3>Open shift — {{ s.branchCode }} <span class="chip c-green">OPEN</span></h3>
        <p class="hint">Opened {{ s.openedAtUtc | date: 'yyyy-MM-dd HH:mm' }} · float {{ s.openingFloat }} {{ s.currency }}</p>
        <form (ngSubmit)="close(s)" style="margin-top:10px">
          <div class="f-row">
            <div class="f" style="flex:0 0 220px">
              <label for="declared">COUNTED DRAWER CASH</label>
              <input id="declared" type="number" step="0.01" [formControl]="declaredCash">
            </div>
            <div class="f" style="flex:0 0 auto; align-self:flex-end">
              <button class="btn green" type="submit" [disabled]="declaredCash.invalid || busy()">
                Close shift — Z-report
              </button>
            </div>
          </div>
        </form>
      </div>
    } @else {
      <div class="card">
        <h3>Open a shift</h3>
        <form (ngSubmit)="open()">
          <div class="f-row">
            <div class="f" style="flex:0 0 260px">
              <label for="branch">BRANCH</label>
              <select id="branch" [formControl]="branchId">
                @for (b of branches(); track b.id) {
                  <option [value]="b.id">{{ b.code }} — {{ b.name }}</option>
                }
              </select>
            </div>
            <div class="f" style="flex:0 0 200px">
              <label for="float">OPENING FLOAT (EGP)</label>
              <input id="float" type="number" step="0.01" [formControl]="openingFloat">
            </div>
            <div class="f" style="flex:0 0 auto; align-self:flex-end">
              <button class="btn" type="submit" [disabled]="!branchId.value || openingFloat.invalid || busy()">
                Open shift
              </button>
            </div>
          </div>
        </form>
      </div>
    }

    @if (zReport(); as z) {
      <div class="card">
        <h3>Z-Report — {{ z.shift.branchCode }} <span class="chip c-blue mono">{{ z.shift.closedAtUtc | date: 'yyyy-MM-dd HH:mm' }}</span></h3>
        <table class="t">
          <tr><th>Method</th><th>Captured</th><th>Refunded</th></tr>
          @for (m of z.byMethod; track m.method) {
            <tr><td class="mono">{{ m.method }}</td><td>{{ m.captured }}</td><td>{{ m.refunded }}</td></tr>
          }
          @if (z.byMethod.length === 0) { <tr><td colspan="3" class="hint">No payments in this shift.</td></tr> }
        </table>
        <table class="t" style="margin-top:10px">
          <tr><td>Opening float</td><td class="mono">{{ z.shift.openingFloat }}</td></tr>
          <tr><td>Cash in − out</td><td class="mono">{{ z.cashIn }} − {{ z.cashOut }}</td></tr>
          <tr><td><b>Expected cash</b></td><td class="mono"><b>{{ z.expectedCash }}</b></td></tr>
          <tr><td><b>Declared (counted)</b></td><td class="mono"><b>{{ z.declaredCash }}</b></td></tr>
          <tr>
            <td><b>Variance</b></td>
            <td class="mono">
              <span class="chip" [class.c-green]="z.variance === 0" [class.c-red]="z.variance !== 0">
                {{ z.variance }}
              </span>
            </td>
          </tr>
        </table>
      </div>
    }

    <div class="card">
      <h3>Shift history</h3>
      <table class="t">
        <tr><th>Branch</th><th>Status</th><th>Opened</th><th>Closed</th><th>Float</th><th>Expected</th><th>Declared</th><th>Variance</th></tr>
        @for (s of shifts(); track s.id) {
          <tr>
            <td class="mono">{{ s.branchCode }}</td>
            <td><span class="chip" [class.c-green]="s.status === 'Open'">{{ s.status }}</span></td>
            <td>{{ s.openedAtUtc | date: 'MM-dd HH:mm' }}</td>
            <td>{{ s.closedAtUtc ? (s.closedAtUtc | date: 'MM-dd HH:mm') : '—' }}</td>
            <td class="mono">{{ s.openingFloat }}</td>
            <td class="mono">{{ s.expectedCash ?? '—' }}</td>
            <td class="mono">{{ s.declaredCash ?? '—' }}</td>
            <td class="mono">{{ s.variance ?? '—' }}</td>
          </tr>
        }
        @if (shifts().length === 0) { <tr><td colspan="8" class="hint">No shifts yet.</td></tr> }
      </table>
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class CashierComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly orgApi = inject(OrgApi);
  private readonly fb = inject(FormBuilder);

  readonly branches = signal<Branch[]>([]);
  readonly shifts = signal<Shift[]>([]);
  readonly zReport = signal<ZReport | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly openShift = computed(() => this.shifts().find(s => s.status === 'Open') ?? null);

  readonly branchId = this.fb.nonNullable.control('');
  readonly openingFloat = this.fb.nonNullable.control(0, [Validators.required, Validators.min(0)]);
  readonly declaredCash = this.fb.nonNullable.control(0, [Validators.required, Validators.min(0)]);

  ngOnInit(): void {
    void this.reload();
  }

  async reload(): Promise<void> {
    try {
      const [branches, shifts] = await Promise.all([
        firstValueFrom(this.orgApi.listBranches()),
        firstValueFrom(this.http.get<Shift[]>(`${API_BASE_URL}/billing/shifts`)),
      ]);
      this.branches.set(branches.filter(b => b.isActive));
      this.shifts.set(shifts);
      if (!this.branchId.value && branches.length > 0) {
        this.branchId.setValue((branches.find(b => b.isMain) ?? branches[0]).id);
      }
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async open(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/billing/shifts`, {
        branchId: this.branchId.value, openingFloat: this.openingFloat.value, currency: 'EGP',
      }));
      this.zReport.set(null);
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  async close(shift: Shift): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const z = await firstValueFrom(this.http.post<ZReport>(
        `${API_BASE_URL}/billing/shifts/${shift.id}/close`, { declaredCash: this.declaredCash.value }));
      this.zReport.set(z);
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
