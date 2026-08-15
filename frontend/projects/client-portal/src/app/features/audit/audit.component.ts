import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { problemMessage } from '../../core/api.types';

interface AuditEvent {
  id: string;
  action: string;
  entityType: string;
  entityId: string;
  oldValues: string | null;
  newValues: string | null;
  userId: string | null;
  ipAddress: string | null;
  occurredAtUtc: string;
  hash: string;
  previousHash: string;
}

interface ChainVerification {
  valid: boolean;
  eventCount: number;
  firstBrokenEventId: string | null;
  detail: string | null;
}

/** FR-SYS-001 audit explorer + hash-chain tamper-evidence check (Quality module view). */
@Component({
  selector: 'app-audit',
  imports: [FormsModule, DatePipe],
  template: `
    <h1 class="pt">Audit Trail</h1>
    <p class="sub">FR-SYS-001 — append-only, written in-transaction with every change, hash-chained per tenant for tamper evidence.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (verification(); as v) {
      @if (v.valid) {
        <div class="note">🔗 Chain integrity verified — {{ v.eventCount }} events, every link intact.</div>
      } @else {
        <div class="err">⛓️‍💥 <b>CHAIN BROKEN</b> at event <span class="mono">{{ v.firstBrokenEventId }}</span> — {{ v.detail }}</div>
      }
    }

    <div class="card">
      <div class="f-row" style="align-items:flex-end">
        <div class="f"><label for="etype">ENTITY TYPE</label>
          <input id="etype" [(ngModel)]="entityType" placeholder="Visit · Patient · TestResult · LabReport…"></div>
        <div class="f"><label for="eid">ENTITY ID</label>
          <input id="eid" class="mono" [(ngModel)]="entityId" placeholder="GUID"></div>
        <div class="f" style="flex:0 0 auto">
          <button class="btn" (click)="load()" [disabled]="loading()">Search</button>
          <button class="btn ghost" style="margin-left:6px" (click)="verify()" [disabled]="loading()">🔗 Verify chain</button>
        </div>
      </div>
      @if (rows().length === 0 && !loading()) { <p class="hint">No audit events match.</p> }
      @else {
        <table class="t">
          <tr><th>When (UTC)</th><th>Action</th><th>Entity</th><th>Changes</th><th>Who / Where</th><th>Link</th></tr>
          @for (a of rows(); track a.id) {
            <tr>
              <td class="mono">{{ a.occurredAtUtc | date: 'MM-dd HH:mm:ss' }}</td>
              <td><span class="chip" [class.c-green]="a.action === 'Created'"
                    [class.c-blue]="a.action === 'Modified'"
                    [class.c-red]="a.action === 'Deleted'">{{ a.action }}</span></td>
              <td><b>{{ a.entityType }}</b><br><span class="mono hint">{{ a.entityId.slice(0, 13) }}…</span></td>
              <td style="max-width:340px">
                @if (a.oldValues) { <div class="vals old">− {{ a.oldValues.slice(0, 160) }}</div> }
                @if (a.newValues) { <div class="vals new">+ {{ a.newValues.slice(0, 160) }}</div> }
              </td>
              <td class="mono hint">{{ a.userId ? a.userId.slice(0, 8) : 'system' }}<br>{{ a.ipAddress ?? '—' }}</td>
              <td class="mono hint" title="hash / previous">{{ a.hash.slice(0, 10) }}…<br>↑ {{ a.previousHash.slice(0, 10) }}…</td>
            </tr>
          }
        </table>
      }
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    .vals { font-family: Consolas, monospace; font-size: 10px; word-break: break-all; }
    .vals.old { color: var(--red); }
    .vals.new { color: var(--green); }
  `,
})
export class AuditComponent implements OnInit {
  private readonly http = inject(HttpClient);

  readonly rows = signal<AuditEvent[]>([]);
  readonly verification = signal<ChainVerification | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  entityType = '';
  entityId = '';

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const params: Record<string, string> = {};
      if (this.entityType) params['entityType'] = this.entityType;
      if (this.entityId) params['entityId'] = this.entityId;
      this.rows.set(await firstValueFrom(
        this.http.get<AuditEvent[]>(`${API_BASE_URL}/audit/events`, { params })));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  async verify(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.verification.set(await firstValueFrom(
        this.http.get<ChainVerification>(`${API_BASE_URL}/audit/verify-chain`)));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
