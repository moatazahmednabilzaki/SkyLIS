import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EnteredResult, PendingEntry, ResultsApi } from './results.api';
import { problemMessage } from '../../core/api.types';
import { firstValueFrom } from 'rxjs';

/** P09.1 Result Entry Workbench: pending lines with inline value entry and live flag feedback. */
@Component({
  selector: 'app-results-entry',
  imports: [FormsModule],
  template: `
    <h1 class="pt">Results Entry</h1>
    <p class="sub">M09 · P09.1 — manual entry with rule flags (range · critical · delta · absurd). Auto-verification for clean in-range results.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (lastEntered(); as r) {
      <div class="note">
        {{ r.testCode }} = {{ r.value }} {{ r.unit }} →
        <b>{{ r.flag }}</b>{{ r.deltaFlagged ? ' · Δ delta-flagged' : '' }}
        · status {{ r.status }}{{ r.autoVerified ? ' (auto-verified ✓)' : '' }}
        @if (r.criticalFlagged) { <b> · 🚨 CRITICAL — documented call required (Critical Values console)</b> }
        @if (r.previousValue !== null) { <span> · previous {{ r.previousValue }}</span> }
      </div>
    }

    <div class="card">
      <div style="display:flex; align-items:center; margin-bottom:10px">
        <h3 style="margin:0">Awaiting result — samples received</h3>
        <span style="flex:1"></span>
        <button class="btn ghost sm" (click)="load()" [disabled]="loading()">Refresh</button>
      </div>
      @if (rows().length === 0 && !loading()) {
        <p class="hint">Nothing pending — lines appear here once their sample is received at accessioning.</p>
      } @else {
        <table class="t">
          <tr><th>Visit</th><th>Patient</th><th>Test</th><th>Sample</th><th>Reference</th><th>Value</th><th></th></tr>
          @for (row of rows(); track row.visitTestId) {
            <tr>
              <td class="mono">{{ row.visitNumber }} @if (row.isStat) { <span class="chip c-red">STAT</span> }</td>
              <td>{{ row.patientName }}</td>
              <td class="mono"><b>{{ row.testCode }}</b></td>
              <td class="mono">{{ row.sampleBarcode }}</td>
              <td class="mono">{{ row.refLow ?? '·' }}–{{ row.refHigh ?? '·' }} {{ row.unit ?? '' }}</td>
              <td style="width:110px">
                <input type="number" step="0.01" [(ngModel)]="values[row.visitTestId]"
                       class="mono" style="width:100px; border:1px solid var(--line); border-radius:6px; padding:5px 8px">
              </td>
              <td><button class="btn sm" [disabled]="busy() || values[row.visitTestId] == null"
                          (click)="enter(row)">Enter ⏎</button></td>
            </tr>
          }
        </table>
      }
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class ResultsEntryComponent implements OnInit {
  private readonly api = inject(ResultsApi);

  readonly rows = signal<PendingEntry[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly lastEntered = signal<EnteredResult | null>(null);
  readonly values: Record<string, number | null> = {};

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.rows.set(await firstValueFrom(this.api.pendingEntry()));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  async enter(row: PendingEntry): Promise<void> {
    const value = this.values[row.visitTestId];
    if (value == null) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      this.lastEntered.set(await firstValueFrom(this.api.enter(row.visitId, row.visitTestId, value)));
      delete this.values[row.visitTestId];
      await this.load();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
