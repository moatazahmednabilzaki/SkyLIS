import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ResultQueueItem, ResultsApi } from './results.api';
import { problemMessage } from '../../core/api.types';

/** P09.2 Technical Validation Queue + P09.3 Medical Sign-Out (SoD + e-signature intent). */
@Component({
  selector: 'app-validation',
  imports: [FormsModule],
  template: `
    <h1 class="pt">Validation &amp; Sign-Out</h1>
    <p class="sub">M09 · P09.2 / P09.3 — two-tier validation. Segregation of duties: the enterer can never medically validate their own result.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note">{{ info() }}</div> }

    <div class="tabs-row">
      <button class="btn sm" [class.ghost]="tab() !== 'tech'" (click)="tab.set('tech')">
        Technical Queue ({{ technical().length }})</button>
      <button class="btn sm" [class.ghost]="tab() !== 'med'" (click)="tab.set('med')">
        Medical Sign-Out ({{ medical().length }})</button>
      <span style="flex:1"></span>
      <button class="btn ghost sm" (click)="load()" [disabled]="loading()">Refresh</button>
    </div>

    @if (tab() === 'tech') {
      <div class="card">
        <h3>Entered results awaiting technical review</h3>
        @if (technical().length === 0) { <p class="hint">Queue is clear — clean in-range results auto-verify.</p> }
        @else {
          <table class="t">
            <tr><th>Visit</th><th>Patient</th><th>Test</th><th>Value</th><th>Flags</th><th>Previous</th><th>Actions</th></tr>
            @for (r of technical(); track r.resultId) {
              <tr>
                <td class="mono">{{ r.visitNumber }}</td>
                <td>{{ r.patientName }}</td>
                <td class="mono"><b>{{ r.testCode }}</b></td>
                <td class="mono">{{ r.value }} {{ r.unit }}</td>
                <td>
                  <span class="chip" [class.c-green]="r.flag === 'Normal'"
                        [class.c-amber]="r.flag === 'Low' || r.flag === 'High'"
                        [class.c-red]="r.flag.startsWith('Critical')">{{ r.flag }}</span>
                  @if (r.deltaFlagged) { <span class="chip c-red">Δ delta</span> }
                </td>
                <td class="mono">{{ r.previousValue ?? '—' }}</td>
                <td>
                  <button class="btn sm green" [disabled]="busy()" (click)="accept(r)">Accept</button>
                  <button class="btn sm ghost" [disabled]="busy()" (click)="rerun(r)">Rerun…</button>
                </td>
              </tr>
            }
          </table>
        }
      </div>
    } @else {
      <div class="card">
        <h3>Technically valid — awaiting medical sign-out</h3>
        @if (medical().length === 0) { <p class="hint">Nothing awaiting sign-out.</p> }
        @else {
          <table class="t">
            <tr><th>Visit</th><th>Patient</th><th>Test</th><th>Value</th><th>Flag</th><th>Interpretive comment</th><th></th></tr>
            @for (r of medical(); track r.resultId) {
              <tr>
                <td class="mono">{{ r.visitNumber }}</td>
                <td>{{ r.patientName }}</td>
                <td class="mono"><b>{{ r.testCode }}</b></td>
                <td class="mono">{{ r.value }} {{ r.unit }}</td>
                <td><span class="chip" [class.c-green]="r.flag === 'Normal'"
                      [class.c-amber]="r.flag !== 'Normal'">{{ r.flag }}</span></td>
                <td><input [(ngModel)]="comments[r.resultId]" placeholder="Optional interpretation…"
                           style="width:100%; border:1px solid var(--line); border-radius:6px; padding:5px 8px"></td>
                <td><button class="btn sm green" [disabled]="busy()" (click)="sign(r)">🖊 Sign</button></td>
              </tr>
            }
          </table>
          <p class="hint">Signing binds your identity, the record version, a timestamp, and the content hash (FR-SYS-002). OIDC re-authentication replaces the dev intent declaration in later phases.</p>
        }
      </div>
    }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    .tabs-row { display: flex; gap: 8px; margin-bottom: 14px; align-items: center; }
  `,
})
export class ValidationComponent implements OnInit {
  private readonly api = inject(ResultsApi);

  readonly tab = signal<'tech' | 'med'>('tech');
  readonly technical = signal<ResultQueueItem[]>([]);
  readonly medical = signal<ResultQueueItem[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);
  readonly comments: Record<string, string> = {};

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [tech, med] = await Promise.all([
        firstValueFrom(this.api.technicalQueue()),
        firstValueFrom(this.api.medicalQueue()),
      ]);
      this.technical.set(tech);
      this.medical.set(med);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  async accept(row: ResultQueueItem): Promise<void> {
    await this.act(() => firstValueFrom(this.api.acceptTechnical(row.resultId)),
      `${row.testCode} accepted → Technically Valid`);
  }

  async rerun(row: ResultQueueItem): Promise<void> {
    const reason = window.prompt(`Rerun reason for ${row.testCode}:`);
    if (!reason) return;
    await this.act(() => firstValueFrom(this.api.rerun(row.resultId, reason)),
      `${row.testCode} rerun ordered — the line returned to Pending`);
  }

  async sign(row: ResultQueueItem): Promise<void> {
    const intent = `I medically validate ${row.testCode} = ${row.value} ${row.unit} for ${row.patientName}`;
    if (!window.confirm(`${intent}.\n\nSign this result?`)) return;
    await this.act(() => firstValueFrom(this.api.validateMedical(
      row.resultId, this.comments[row.resultId] || null, intent)),
      `${row.testCode} signed → Medically Valid (released to reporting)`);
  }

  private async act(action: () => Promise<unknown>, successMessage: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await action();
      this.info.set(successMessage);
      await this.load();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
