import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { problemMessage } from '../../core/api.types';
import { RealtimeService } from '../../core/realtime.service';

interface CollectionItem {
  visitId: string; sampleId: string; barcode: string; visitNumber: string;
  patientName: string; isStat: boolean; isRecollection: boolean; condition: string | null;
}
interface UpcomingReservation {
  sampleId: string; barcode: string; visitNumber: string; patientName: string;
  condition: string | null; readyAtUtc: string;
}
interface Worklist {
  toCollect: CollectionItem[];
  upcomingReservations: UpcomingReservation[];
}

/** P08.2 Phlebotomist Worklist — the collection queue with opened reservation windows and recollections. */
@Component({
  selector: 'app-phlebotomist',
  imports: [DatePipe],
  template: `
    <h1 class="pt">Phlebotomist Worklist</h1>
    <p class="sub">M08 · P08.2 — ready-to-collect queue (STAT first), opened condition windows, and recollections. Live via SignalR.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note">{{ info() }}</div> }

    @if (data(); as d) {
      <div class="card">
        <div style="display:flex; align-items:center; margin-bottom:10px">
          <h3 style="margin:0">To collect ({{ d.toCollect.length }})</h3>
          <span style="flex:1"></span>
          <button class="btn ghost sm" (click)="load()">Refresh</button>
        </div>
        @if (d.toCollect.length === 0) { <p class="hint">Queue is clear 🎉</p> }
        @else {
          <table class="t">
            <tr><th>Sample</th><th>Patient</th><th>Flags</th><th>Condition</th><th></th></tr>
            @for (c of d.toCollect; track c.sampleId) {
              <tr>
                <td class="mono"><b>{{ c.barcode }}</b></td>
                <td><b>{{ c.patientName }}</b> <span class="hint mono">{{ c.visitNumber }}</span></td>
                <td>
                  @if (c.isStat) { <span class="chip c-red">STAT</span> }
                  @if (c.isRecollection) { <span class="chip c-amber">Recollection</span> }
                </td>
                <td>{{ c.condition ?? '—' }}</td>
                <td><button class="btn sm green" [disabled]="busy()" (click)="collect(c)">
                  Scan-confirm &amp; collect ✓</button></td>
              </tr>
            }
          </table>
          <p class="hint">Identity verification (name + DOB spoken back) precedes every draw; scanning another patient's label hard-blocks server-side.</p>
        }
      </div>

      <div class="card">
        <h3>Upcoming reservations ({{ d.upcomingReservations.length }})</h3>
        @if (d.upcomingReservations.length === 0) { <p class="hint">No condition windows pending.</p> }
        @else {
          <table class="t">
            <tr><th>Sample</th><th>Patient</th><th>Condition</th><th>Window opens (UTC)</th></tr>
            @for (u of d.upcomingReservations; track u.sampleId) {
              <tr>
                <td class="mono">{{ u.barcode }}</td>
                <td><b>{{ u.patientName }}</b> <span class="hint mono">{{ u.visitNumber }}</span></td>
                <td>{{ u.condition ?? '—' }}</td>
                <td class="mono">{{ u.readyAtUtc | date: 'HH:mm' }}</td>
              </tr>
            }
          </table>
        }
      </div>
    } @else if (!error()) { <p class="sub">Loading…</p> }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class PhlebotomistComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);

  readonly data = signal<Worklist | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
    this.realtime.onArea('phleb')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => void this.load());
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      this.data.set(await firstValueFrom(this.http.get<Worklist>(`${API_BASE_URL}/worklists/phlebotomist`)));
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async collect(item: CollectionItem): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.post(
        `${API_BASE_URL}/visits/${item.visitId}/samples/${item.sampleId}/collect`, {}));
      this.info.set(`${item.barcode} collected ✓ (${item.patientName})`);
      await this.load();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
