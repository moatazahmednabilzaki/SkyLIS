import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { problemMessage } from '../../core/api.types';

interface TatRow { testCode: string; department: string; count: number; medianMinutes: number; p90Minutes: number; }
interface MoneyByKey { key: string; amount: number; }
interface Financial {
  totalCaptured: number; totalRefunded: number; netRevenue: number; currency: string;
  byMethod: MoneyByKey[]; byBranch: MoneyByKey[]; byDay: MoneyByKey[];
}
interface Quality {
  samplesTotal: number; samplesRejected: number; rejectionRatePercent: number;
  byReason: { reasonCode: string; count: number }[];
  criticalValues: number; criticalsClosed: number; amendedResults: number; rerunsOrdered: number;
}
interface Detail { fromDay: string; toDay: string; tat: TatRow[]; financial: Financial; quality: Quality; }

/** M23 · P23.2–P23.4 — TAT, financial, and quality analysis over the last 30 days. */
@Component({
  selector: 'app-analytics',
  template: `
    <h1 class="pt">Analytics</h1>
    @if (detail(); as d) {
      <p class="sub">M23 · P23.2–P23.4 — window {{ d.fromDay }} → {{ d.toDay }}.</p>

      <div class="card">
        <h3>⏱️ Turnaround time per test (P23.2)</h3>
        <table class="t">
          <tr><th>Test</th><th>Department</th><th>Signed out</th><th>Median (min)</th><th>P90 (min)</th></tr>
          @for (r of d.tat; track r.testCode) {
            <tr>
              <td class="mono"><b>{{ r.testCode }}</b></td>
              <td>{{ r.department }}</td>
              <td class="mono">{{ r.count }}</td>
              <td class="mono">{{ r.medianMinutes }}</td>
              <td class="mono">{{ r.p90Minutes }}</td>
            </tr>
          }
          @if (d.tat.length === 0) { <tr><td colspan="5" class="hint">No signed-out results in the window.</td></tr> }
        </table>
        <p class="hint">Register → medical sign-out. The register → delivered TAT lives on the dashboard.</p>
      </div>

      <div class="card">
        <h3>💰 Financial (P23.3)</h3>
        <div class="kpis">
          <div class="kpi"><div class="v">{{ d.financial.totalCaptured }} {{ d.financial.currency }}</div><div class="l">Captured</div></div>
          <div class="kpi"><div class="v red">−{{ d.financial.totalRefunded }}</div><div class="l">Refunded</div></div>
          <div class="kpi"><div class="v green">{{ d.financial.netRevenue }} {{ d.financial.currency }}</div><div class="l">Net revenue</div></div>
        </div>
        <div class="cols">
          <table class="t">
            <tr><th>Method</th><th>Net</th></tr>
            @for (m of d.financial.byMethod; track m.key) {
              <tr><td class="mono">{{ m.key }}</td><td class="mono">{{ m.amount }}</td></tr>
            }
          </table>
          <table class="t">
            <tr><th>Branch</th><th>Net</th></tr>
            @for (m of d.financial.byBranch; track m.key) {
              <tr><td class="mono">{{ m.key }}</td><td class="mono">{{ m.amount }}</td></tr>
            }
          </table>
          <table class="t">
            <tr><th>Day</th><th>Net</th></tr>
            @for (m of d.financial.byDay; track m.key) {
              <tr><td class="mono">{{ m.key }}</td><td class="mono">{{ m.amount }}</td></tr>
            }
          </table>
        </div>
      </div>

      <div class="card">
        <h3>🧪 Quality (P23.4)</h3>
        <div class="kpis">
          <div class="kpi"><div class="v">{{ d.quality.samplesTotal }}</div><div class="l">Samples collected</div></div>
          <div class="kpi"><div class="v" [class.red]="d.quality.rejectionRatePercent > 2">{{ d.quality.rejectionRatePercent }}%</div><div class="l">Rejection rate ({{ d.quality.samplesRejected }})</div></div>
          <div class="kpi"><div class="v">{{ d.quality.criticalsClosed }}/{{ d.quality.criticalValues }}</div><div class="l">Criticals closed</div></div>
          <div class="kpi"><div class="v">{{ d.quality.amendedResults }}</div><div class="l">Amended results</div></div>
          <div class="kpi"><div class="v">{{ d.quality.rerunsOrdered }}</div><div class="l">Reruns ordered</div></div>
        </div>
        <table class="t">
          <tr><th>Rejection reason</th><th>Count</th></tr>
          @for (r of d.quality.byReason; track r.reasonCode) {
            <tr><td class="mono">{{ r.reasonCode }}</td><td class="mono">{{ r.count }}</td></tr>
          }
          @if (d.quality.byReason.length === 0) { <tr><td colspan="2" class="hint">No rejections 🎉</td></tr> }
        </table>
      </div>
    } @else {
      <p class="sub">Loading…</p>
      @if (error()) { <div class="err">{{ error() }}</div> }
    }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin-bottom: 12px; }
    .kpi { background: #fff; border: 1px solid var(--line); border-radius: 10px; padding: 12px 14px; }
    .kpi .v { font-size: 18px; font-weight: 700; color: var(--navy); }
    .kpi .v.red { color: var(--red); } .kpi .v.green { color: var(--green); }
    .kpi .l { font-size: 11px; color: var(--slate); margin-top: 2px; }
    .cols { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 14px; }
  `,
})
export class AnalyticsComponent implements OnInit {
  private readonly http = inject(HttpClient);

  readonly detail = signal<Detail | null>(null);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      this.detail.set(await firstValueFrom(this.http.get<Detail>(`${API_BASE_URL}/analytics/detail`)));
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }
}
