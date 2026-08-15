import { DatePipe } from '@angular/common';
import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RealtimeService } from '../../core/realtime.service';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { CriticalQueueItem, ResultsApi } from './results.api';
import { problemMessage } from '../../core/api.types';

/** P09.4 Critical Values Console: every panic value documented with read-back confirmation. */
@Component({
  selector: 'app-critical',
  imports: [FormsModule, DatePipe],
  template: `
    <h1 class="pt">Critical Values</h1>
    <p class="sub">M09 · P09.4 — every critical value must reach a responsible caregiver, with read-back documented. A report with an open critical value cannot reach Final.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note">{{ info() }}</div> }

    <div class="card">
      <div style="display:flex; align-items:center; margin-bottom:10px">
        <h3 style="margin:0">Critical queue</h3>
        <span style="flex:1"></span>
        <button class="btn ghost sm" (click)="load()" [disabled]="loading()">Refresh</button>
      </div>
      @if (rows().length === 0 && !loading()) { <p class="hint">No critical values 🎉</p> }
      @else {
        <table class="t">
          <tr><th>Patient</th><th>Test</th><th>Value</th><th>Flagged</th><th>State</th><th>Document call</th></tr>
          @for (r of rows(); track r.resultId) {
            <tr>
              <td><b>{{ r.patientName }}</b> <span class="mono hint">{{ r.visitNumber }}</span></td>
              <td class="mono">{{ r.testCode }}</td>
              <td class="mono" style="color:var(--red); font-weight:700">{{ r.value }} {{ r.unit }} ({{ r.flag }})</td>
              <td>{{ r.flaggedAtUtc | date: 'HH:mm' }}</td>
              <td><span class="chip"
                    [class.c-red]="r.criticalState === 'Flagged'"
                    [class.c-amber]="r.criticalState === 'ReadBackDocumented'"
                    [class.c-green]="r.criticalState === 'Closed'">{{ r.criticalState }}</span>
                @if (r.calledPerson) { <span class="hint">{{ r.calledPerson }}</span> }
              </td>
              <td>
                @if (r.criticalState !== 'Closed') {
                  <div style="display:flex; gap:6px; flex-wrap:wrap">
                    <input [(ngModel)]="who[r.resultId]" placeholder="Who was called"
                           style="width:150px; border:1px solid var(--line); border-radius:6px; padding:5px 8px">
                    <input [(ngModel)]="phone[r.resultId]" placeholder="Phone" class="mono"
                           style="width:120px; border:1px solid var(--line); border-radius:6px; padding:5px 8px">
                    <label style="font-size:11px; display:flex; align-items:center; gap:4px">
                      <input type="checkbox" [(ngModel)]="readBack[r.resultId]"> read-back ✓
                    </label>
                    <button class="btn sm danger" [disabled]="busy()" (click)="documentCall(r)">Document</button>
                  </div>
                } @else { <span class="hint">closed ✓</span> }
              </td>
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
export class CriticalComponent implements OnInit {
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ResultsApi);

  readonly rows = signal<CriticalQueueItem[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);
  readonly who: Record<string, string> = {};
  readonly phone: Record<string, string> = {};
  readonly readBack: Record<string, boolean> = {};

  ngOnInit(): void {
    void this.load();
    this.realtime.onArea('critical')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => void this.load());
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.rows.set(await firstValueFrom(this.api.criticalQueue()));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  async documentCall(row: CriticalQueueItem): Promise<void> {
    const calledPerson = this.who[row.resultId];
    const calledPhone = this.phone[row.resultId];
    if (!calledPerson || !calledPhone) {
      this.error.set('The called person and phone number are mandatory evidence.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.api.documentCriticalCall(
        row.resultId, calledPerson, calledPhone, this.readBack[row.resultId] ?? false));
      this.info.set(this.readBack[row.resultId]
        ? `${row.testCode} closed with read-back evidence ✓`
        : `${row.testCode} call documented — stays open until read-back is confirmed`);
      await this.load();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
