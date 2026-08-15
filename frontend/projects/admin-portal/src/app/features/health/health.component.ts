import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/auth.service';

interface OutboxFailure {
  id: string;
  eventType: string;
  attempts: number;
  lastError: string | null;
  occurredAtUtc: string;
}

interface OutboxStatus {
  pending: number;
  processed: number;
  poisoned: number;
  recentFailures: OutboxFailure[];
}

/** P01.6 Platform Health: outbox dispatch status (FR-SYS-010 monitored background processing). */
@Component({
  selector: 'app-health',
  imports: [DatePipe],
  template: `
    <h1 class="pt">Platform Health</h1>
    <p class="sub">M01 · P01.6 — reliable-event pipeline status. Poisoned messages exhausted their retries and need engineering attention.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (status(); as s) {
      <div class="kpis">
        <div class="kpi"><div class="v" [class.amber]="s.pending > 10">{{ s.pending }}</div><div class="l">Outbox pending</div></div>
        <div class="kpi"><div class="v green">{{ s.processed }}</div><div class="l">Processed (at-least-once)</div></div>
        <div class="kpi"><div class="v" [class.red]="s.poisoned > 0">{{ s.poisoned }}</div><div class="l">Poisoned</div></div>
      </div>
      <div class="card">
        <div style="display:flex; align-items:center; margin-bottom:10px">
          <h3 style="margin:0">Recent failures</h3>
          <span style="flex:1"></span>
          <button class="btn ghost sm" (click)="load()">Refresh</button>
        </div>
        @if (s.recentFailures.length === 0) { <p class="hint">No failures on record 🎉</p> }
        @else {
          <table class="t">
            <tr><th>Event</th><th>Attempts</th><th>Last error</th><th>Occurred</th></tr>
            @for (f of s.recentFailures; track f.id) {
              <tr>
                <td class="mono">{{ f.eventType.split('.').pop() }}</td>
                <td class="mono">{{ f.attempts }}</td>
                <td style="max-width:400px">{{ f.lastError }}</td>
                <td>{{ f.occurredAtUtc | date: 'MM-dd HH:mm' }}</td>
              </tr>
            }
          </table>
        }
      </div>
    } @else if (!error()) { <p class="sub">Loading…</p> }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: #fff; margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 12px; margin-bottom: 18px; }
    .kpi { background: var(--card); border: 1px solid var(--line); border-radius: var(--radius); padding: 13px 15px; }
    .kpi .v { font-size: 24px; font-weight: 700; color: #7dd3fc; }
    .kpi .v.green { color: var(--green); } .kpi .v.red { color: var(--red); } .kpi .v.amber { color: var(--amber); }
    .kpi .l { font-size: 11px; color: var(--slate); margin-top: 2px; }
  `,
})
export class HealthComponent implements OnInit {
  private readonly http = inject(HttpClient);

  readonly status = signal<OutboxStatus | null>(null);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      this.status.set(await firstValueFrom(
        this.http.get<OutboxStatus>(`${API_BASE_URL}/platform/outbox/status`)));
    } catch {
      this.error.set('Could not load outbox status. Is the API running?');
    }
  }
}
