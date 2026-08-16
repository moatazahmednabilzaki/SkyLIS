import { DatePipe } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { problemMessage } from '../../core/api.types';

interface Patient360Visit {
  visitId: string;
  visitNumber: string;
  branchCode: string;
  registeredAtUtc: string;
  status: string;
  isStat: boolean;
  invoiceId: string;
  invoiceStatus: string;
  total: number;
  balance: number;
  currency: string;
}

interface Patient360Report {
  reportId: string;
  reportNumber: string;
  version: number;
  kind: string;
  status: string;
  renderedAtUtc: string;
}

interface Patient360 {
  id: string;
  patientNumber: string;
  fullName: string;
  gender: string;
  dateOfBirth: string;
  age: number;
  mobile: string;
  nationalId: string | null;
  registeredAtUtc: string;
  lastVisitAtUtc: string | null;
  outstandingBalance: number;
  currency: string;
  visits: Patient360Visit[];
  reports: Patient360Report[];
  testCodes: string[];
}

interface CumulativePoint {
  resultId: string;
  visitNumber: string;
  value: number;
  unit: string;
  flag: string;
  isAmended: boolean;
  validatedAtUtc: string;
  refLow: number | null;
  refHigh: number | null;
}

/**
 * P04.3 Patient 360: demographics, visit & financial history, reports — plus the
 * P10.3 cumulative trend per test and the P09.5 amendment action for authorized users.
 */
@Component({
  selector: 'app-patient-360',
  imports: [DatePipe, RouterLink, ReactiveFormsModule],
  template: `
    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (data(); as p) {
      <h1 class="pt">{{ p.fullName }} <span class="chip c-blue mono">{{ p.patientNumber }}</span>
        <button class="btn sm ghost" style="margin-left:10px" (click)="exportData()">Export data (P04.5)</button>
        <button class="btn sm danger" (click)="requestErasure()">Request erasure…</button>
      </h1>
      <p class="sub">M04 · P04.3 Patient 360 — the complete story of one patient.</p>
      @if (info()) { <div class="note">{{ info() }}</div> }

      <div class="kpis">
        <div class="kpi"><div class="v">{{ p.gender }} · {{ p.age }}y</div><div class="l">Born {{ p.dateOfBirth }}</div></div>
        <div class="kpi"><div class="v mono">{{ p.mobile }}</div><div class="l">Mobile{{ p.nationalId ? ' · NID ' + p.nationalId : '' }}</div></div>
        <div class="kpi"><div class="v">{{ p.visits.length }}</div><div class="l">Visits since {{ p.registeredAtUtc | date: 'yyyy-MM-dd' }}</div></div>
        <div class="kpi"><div class="v" [class.red]="p.outstandingBalance > 0">{{ p.outstandingBalance }} {{ p.currency }}</div><div class="l">Outstanding balance</div></div>
      </div>

      <div class="card">
        <h3>Visit history</h3>
        <table class="t">
          <tr><th>Visit</th><th>Branch</th><th>Date</th><th>Status</th><th>Invoice</th><th>Total</th><th>Balance</th><th></th></tr>
          @for (v of p.visits; track v.visitId) {
            <tr>
              <td class="mono">{{ v.visitNumber }} @if (v.isStat) { <span class="chip c-red">STAT</span> }</td>
              <td class="mono">{{ v.branchCode }}</td>
              <td>{{ v.registeredAtUtc | date: 'yyyy-MM-dd HH:mm' }}</td>
              <td><span class="chip">{{ v.status }}</span></td>
              <td><span class="chip" [class.c-green]="v.invoiceStatus === 'Paid'"
                        [class.c-amber]="v.invoiceStatus === 'PartiallyPaid'">{{ v.invoiceStatus }}</span></td>
              <td class="mono">{{ v.total }}</td>
              <td class="mono">{{ v.balance }}</td>
              <td><a class="btn sm ghost" [routerLink]="['/visits', v.visitId]">Open →</a></td>
            </tr>
          }
        </table>
      </div>

      <div class="card">
        <h3>Cumulative results (P10.3)</h3>
        @if (p.testCodes.length === 0) {
          <p class="hint">No validated results yet.</p>
        } @else {
          <div class="f-row">
            <div class="f" style="flex:0 0 220px">
              <label for="tc">TEST</label>
              <select id="tc" [formControl]="testCode" (change)="loadTrend()">
                @for (code of p.testCodes; track code) { <option [value]="code">{{ code }}</option> }
              </select>
            </div>
          </div>
          <table class="t">
            <tr><th>Visit</th><th>Validated</th><th>Value</th><th>Reference</th><th>Flag</th><th></th></tr>
            @for (point of trend(); track point.resultId) {
              <tr>
                <td class="mono">{{ point.visitNumber }}</td>
                <td>{{ point.validatedAtUtc | date: 'yyyy-MM-dd HH:mm' }}</td>
                <td class="mono"><b>{{ point.value }}</b> {{ point.unit }}
                  @if (point.isAmended) { <span class="chip c-red">AMENDED</span> }
                </td>
                <td class="mono">{{ point.refLow ?? '·' }}–{{ point.refHigh ?? '·' }}</td>
                <td><span class="chip" [class.c-green]="point.flag === 'Normal'"
                          [class.c-red]="point.flag.startsWith('Critical')"
                          [class.c-amber]="point.flag === 'High' || point.flag === 'Low'">{{ point.flag }}</span></td>
                <td><button class="btn sm ghost" (click)="amend(point)">Amend…</button></td>
              </tr>
            }
            @if (trend().length === 0) { <tr><td colspan="6" class="hint">No points for this test.</td></tr> }
          </table>
          <p class="hint">Amendments (P09.5) require validation authority; the previous value stays on record and later reports render as AMENDED.</p>
        }
      </div>

      <div class="card">
        <h3>Reports</h3>
        <table class="t">
          <tr><th>Number</th><th>Version</th><th>Kind</th><th>Status</th><th>Rendered</th></tr>
          @for (r of p.reports; track r.reportId) {
            <tr>
              <td class="mono">{{ r.reportNumber }}</td>
              <td class="mono">v{{ r.version }}</td>
              <td><span class="chip" [class.c-green]="r.kind === 'Final'"
                        [class.c-amber]="r.kind === 'Interim'"
                        [class.c-red]="r.kind === 'Amended'">{{ r.kind }}</span></td>
              <td>{{ r.status }}</td>
              <td>{{ r.renderedAtUtc | date: 'yyyy-MM-dd HH:mm' }}</td>
            </tr>
          }
          @if (p.reports.length === 0) { <tr><td colspan="5" class="hint">No reports yet.</td></tr> }
        </table>
      </div>
    }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; margin-bottom: 18px; }
    .kpi { background: #fff; border: 1px solid var(--line); border-radius: 10px; padding: 13px 15px; }
    .kpi .v { font-size: 17px; font-weight: 700; color: var(--navy); }
    .kpi .v.red { color: var(--red); }
    .kpi .l { font-size: 11px; color: var(--slate); margin-top: 2px; }
  `,
})
export class Patient360Component implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  readonly data = signal<Patient360 | null>(null);
  readonly trend = signal<CumulativePoint[]>([]);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);
  readonly testCode = this.fb.nonNullable.control('');

  ngOnInit(): void {
    void this.load();
  }

  private patientId(): string {
    return this.route.snapshot.paramMap.get('id')!;
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      const p = await firstValueFrom(
        this.http.get<Patient360>(`${API_BASE_URL}/patients/${this.patientId()}/summary`));
      this.data.set(p);
      if (p.testCodes.length > 0) {
        this.testCode.setValue(p.testCodes[0]);
        await this.loadTrend();
      }
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async loadTrend(): Promise<void> {
    if (!this.testCode.value) return;
    try {
      const params = new HttpParams().set('testCode', this.testCode.value);
      this.trend.set(await firstValueFrom(this.http.get<CumulativePoint[]>(
        `${API_BASE_URL}/patients/${this.patientId()}/results/cumulative`, { params })));
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async exportData(): Promise<void> {
    const reason = window.prompt('Export reason (mandatory, audited — P04.5):', 'Patient requested a copy');
    if (!reason) return;
    try {
      const bundle = await firstValueFrom(this.http.post(
        `${API_BASE_URL}/patients/${this.patientId()}/export`, { reason }));
      const blob = new Blob([JSON.stringify(bundle, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `patient-export-${this.data()?.patientNumber ?? this.patientId()}.json`;
      link.click();
      URL.revokeObjectURL(url);
      this.info.set('Export downloaded — the request is logged in the data-subject register.');
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async requestErasure(): Promise<void> {
    const reason = window.prompt('Erasure request reason (P04.5 — approval required, clinical records are retained):');
    if (!reason) return;
    try {
      await firstValueFrom(this.http.post(
        `${API_BASE_URL}/patients/${this.patientId()}/erasure-requests`, { reason }));
      this.info.set('Erasure request logged — pending approval by an authorized user.');
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async amend(point: CumulativePoint): Promise<void> {
    const newValueRaw = window.prompt(
      `Amend ${this.testCode.value} on ${point.visitNumber} (current: ${point.value} ${point.unit}). New value:`);
    if (!newValueRaw) return;
    const reason = window.prompt('Amendment reason (mandatory, audited):');
    if (!reason) return;
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/results/${point.resultId}/amend`, {
        newValue: Number(newValueRaw),
        reason,
        signatureIntent: `I amend ${this.testCode.value} from ${point.value} to ${newValueRaw} ${point.unit}`,
      }));
      await this.loadTrend();
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }
}
