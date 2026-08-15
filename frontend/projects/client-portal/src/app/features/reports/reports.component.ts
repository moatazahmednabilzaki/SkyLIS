import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RealtimeService } from '../../core/realtime.service';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { problemMessage } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';

interface WorklistRow {
  visitId: string;
  visitNumber: string;
  patientName: string;
  visitStatus: string;
  medicallyValidCount: number;
  totalTests: number;
  reportId: string | null;
  reportNumber: string | null;
  version: number | null;
  kind: string | null;
  reportStatus: string | null;
  renderedAtUtc: string | null;
  deliveryCount: number;
}

interface RenderedReport {
  reportId: string;
  reportNumber: string;
  version: number;
  kind: string;
  contentHash: string;
  verificationPath: string;
}

/** P08.3 Reporting Worklist + M10 render/deliver/verify. */
@Component({
  selector: 'app-reports',
  imports: [FormsModule, DatePipe],
  template: `
    <h1 class="pt">Reporting Worklist</h1>
    <p class="sub">M10 · P10.1/P10.2 — immutable hash-stamped artifacts; one FINAL report per visit is the metering unit. A FINAL cannot render with an open critical value.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note" [innerHTML]="info()"></div> }

    <div class="card">
      <div style="display:flex; align-items:center; margin-bottom:10px">
        <h3 style="margin:0">Visits with validated results</h3>
        <span style="flex:1"></span>
        <button class="btn ghost sm" (click)="load()" [disabled]="loading()">Refresh</button>
      </div>
      @if (rows().length === 0 && !loading()) {
        <p class="hint">Nothing here yet — visits appear once at least one result is medically valid.</p>
      } @else {
        <table class="t">
          <tr><th>Visit</th><th>Patient</th><th>Signed</th><th>Latest report</th><th>Deliveries</th><th>Actions</th></tr>
          @for (r of rows(); track r.visitId) {
            <tr>
              <td class="mono">{{ r.visitNumber }}
                <span class="chip" [class.c-green]="r.visitStatus === 'Reported'"
                      [class.c-blue]="r.visitStatus !== 'Reported'">{{ r.visitStatus }}</span></td>
              <td>{{ r.patientName }}</td>
              <td class="mono">{{ r.medicallyValidCount }} / {{ r.totalTests }}</td>
              <td>
                @if (r.reportNumber) {
                  <span class="mono">{{ r.reportNumber }} v{{ r.version }}</span>
                  <span class="chip" [class.c-amber]="r.kind === 'Interim'"
                        [class.c-green]="r.kind === 'Final'">{{ r.kind }}</span>
                  <span class="chip c-navy">{{ r.reportStatus }}</span>
                  <span class="hint">{{ r.renderedAtUtc | date: 'HH:mm' }}</span>
                } @else { <span class="hint">not rendered</span> }
              </td>
              <td class="mono">{{ r.deliveryCount }}</td>
              <td style="white-space:nowrap">
                @if (r.visitStatus === 'Validated' && r.kind !== 'Final') {
                  <button class="btn sm green" [disabled]="busy()" (click)="render(r, 'Final')">Render FINAL</button>
                }
                @if (r.visitStatus !== 'Validated' && r.visitStatus !== 'Reported') {
                  <button class="btn sm" [disabled]="busy()" (click)="render(r, 'Interim')">Render interim</button>
                }
                @if (r.reportId) {
                  <button class="btn sm ghost" (click)="open(r.reportId!)">Open</button>
                  <button class="btn sm ghost" [disabled]="busy()" (click)="deliver(r)">Deliver…</button>
                }
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
export class ReportsComponent implements OnInit {
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  readonly rows = signal<WorklistRow[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
    this.realtime.onArea('reports')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => void this.load());
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.rows.set(await firstValueFrom(this.http.get<WorklistRow[]>(`${API_BASE_URL}/reports/worklist`)));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  async render(row: WorklistRow, kind: 'Interim' | 'Final'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      const report = await firstValueFrom(this.http.post<RenderedReport>(
        `${API_BASE_URL}/visits/${row.visitId}/reports`, { kind }));
      this.info.set(`${report.reportNumber} v${report.version} (${report.kind}) rendered — `
        + `hash <span class="mono">${report.contentHash.slice(0, 16)}…</span> · `
        + `<a href="http://localhost:5178${report.verificationPath}" target="_blank">public verification link</a>`);
      await this.load();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  open(reportId: string): void {
    // The artifact endpoint requires the bearer token; fetch and open as a blob.
    void fetch(`${API_BASE_URL}/reports/${reportId}/content`, {
      headers: { Authorization: `Bearer ${this.auth.token}` },
    })
      .then(r => r.text())
      .then(html => {
        const url = URL.createObjectURL(new Blob([html], { type: 'text/html' }));
        window.open(url, '_blank');
      });
  }

  async deliver(row: WorklistRow): Promise<void> {
    const channel = window.prompt('Channel (print / email / whatsapp / portal):', 'whatsapp');
    if (!channel) return;
    const destination = window.prompt('Destination (phone/email/printer):', '+201002345678');
    if (!destination) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post(
        `${API_BASE_URL}/reports/${row.reportId}/deliver`, { channel, destination }));
      this.info.set(`Delivered via ${channel} to ${destination} ✓ (attempt logged)`);
      await this.load();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
