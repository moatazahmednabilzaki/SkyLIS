import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { problemMessage } from '../../core/api.types';
import { RealtimeService } from '../../core/realtime.service';

interface ReservationDue {
  visitId: string; sampleId: string; barcode: string; visitNumber: string;
  patientName: string; condition: string | null; readyAtUtc: string; windowOpen: boolean;
}
interface PatientInformation {
  visitId: string; sampleId: string; barcode: string; visitNumber: string;
  patientName: string; reasonCode: string; recollectionBarcode: string | null;
}
interface ReportHandout {
  reportId: string; reportNumber: string; visitNumber: string; patientName: string;
  kind: string; renderedAtUtc: string;
}
interface BalanceDue {
  invoiceId: string; invoiceNumber: string; visitNumber: string; patientName: string;
  balance: number; currency: string;
}
interface Worklist {
  reservationsDue: ReservationDue[];
  patientInformation: PatientInformation[];
  reportsToHandOut: ReportHandout[];
  balancesDue: BalanceDue[];
}

/** P08.1 Reception Worklist — everything reception owns right now (merged per SRS Rev 2.0). */
@Component({
  selector: 'app-reception',
  imports: [DatePipe, RouterLink],
  template: `
    <h1 class="pt">Reception Worklist</h1>
    <p class="sub">M08 · P08.1 — reservations due, mandatory patient information after rejections (P07.3), reports to hand out, balances due. Live via SignalR.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note">{{ info() }}</div> }

    @if (data(); as d) {
      <div class="tabs-row">
        <button class="btn sm" [class.ghost]="tab() !== 'res'" (click)="tab.set('res')">Reservations ({{ d.reservationsDue.length }})</button>
        <button class="btn sm" [class.ghost]="tab() !== 'inf'" (click)="tab.set('inf')">Patient Information ({{ d.patientInformation.length }})</button>
        <button class="btn sm" [class.ghost]="tab() !== 'rep'" (click)="tab.set('rep')">Reports Ready ({{ d.reportsToHandOut.length }})</button>
        <button class="btn sm" [class.ghost]="tab() !== 'bal'" (click)="tab.set('bal')">Balance Due ({{ d.balancesDue.length }})</button>
        <span style="flex:1"></span>
        <button class="btn ghost sm" (click)="load()">Refresh</button>
      </div>

      @if (tab() === 'res') {
        <div class="card">
          <h3>Condition reservations</h3>
          @if (d.reservationsDue.length === 0) { <p class="hint">No reserved samples waiting.</p> }
          @else {
            <table class="t">
              <tr><th>Sample</th><th>Patient</th><th>Condition</th><th>Ready at (UTC)</th><th>Status</th></tr>
              @for (r of d.reservationsDue; track r.sampleId) {
                <tr>
                  <td class="mono">{{ r.barcode }}</td>
                  <td><b>{{ r.patientName }}</b> <span class="hint mono">{{ r.visitNumber }}</span></td>
                  <td>{{ r.condition ?? '—' }}</td>
                  <td class="mono">{{ r.readyAtUtc | date: 'HH:mm' }}</td>
                  <td>@if (r.windowOpen) { <span class="chip c-green">Window open — call the patient</span> }
                      @else { <span class="chip c-amber">Waiting</span> }</td>
                </tr>
              }
            </table>
          }
        </div>
      } @else if (tab() === 'inf') {
        <div class="card">
          <h3>Rejections — patient must be informed (mandatory, P07.3)</h3>
          @if (d.patientInformation.length === 0) { <p class="hint">Nothing to communicate 🎉</p> }
          @else {
            <table class="t">
              <tr><th>Sample</th><th>Patient</th><th>Reason</th><th>Recollection</th><th></th></tr>
              @for (i of d.patientInformation; track i.sampleId) {
                <tr>
                  <td class="mono">{{ i.barcode }}</td>
                  <td><b>{{ i.patientName }}</b> <span class="hint mono">{{ i.visitNumber }}</span></td>
                  <td><span class="chip c-red">{{ i.reasonCode }}</span></td>
                  <td class="mono">{{ i.recollectionBarcode ?? '—' }}</td>
                  <td><button class="btn sm" [disabled]="busy()" (click)="markInformed(i)">
                    Mark patient informed ✓</button></td>
                </tr>
              }
            </table>
            <p class="hint">The communication (who informed, when) lands in the audit trail automatically.</p>
          }
        </div>
      } @else if (tab() === 'rep') {
        <div class="card">
          <h3>Rendered reports awaiting handout / delivery</h3>
          @if (d.reportsToHandOut.length === 0) { <p class="hint">Nothing to hand out.</p> }
          @else {
            <table class="t">
              <tr><th>Report</th><th>Patient</th><th>Kind</th><th>Rendered</th><th></th></tr>
              @for (r of d.reportsToHandOut; track r.reportId) {
                <tr>
                  <td class="mono">{{ r.reportNumber }}</td>
                  <td><b>{{ r.patientName }}</b> <span class="hint mono">{{ r.visitNumber }}</span></td>
                  <td><span class="chip" [class.c-green]="r.kind === 'Final'" [class.c-amber]="r.kind === 'Interim'">{{ r.kind }}</span></td>
                  <td>{{ r.renderedAtUtc | date: 'HH:mm' }}</td>
                  <td><a class="btn sm ghost" routerLink="/reports">Open Reporting →</a></td>
                </tr>
              }
            </table>
          }
        </div>
      } @else {
        <div class="card">
          <h3>Open balances</h3>
          @if (d.balancesDue.length === 0) { <p class="hint">All settled 🎉</p> }
          @else {
            <table class="t">
              <tr><th>Invoice</th><th>Patient</th><th style="text-align:right">Balance</th></tr>
              @for (b of d.balancesDue; track b.invoiceId) {
                <tr>
                  <td class="mono">{{ b.invoiceNumber }}</td>
                  <td><b>{{ b.patientName }}</b> <span class="hint mono">{{ b.visitNumber }}</span></td>
                  <td class="mono" style="text-align:right; color:var(--red)">{{ b.balance }} {{ b.currency }}</td>
                </tr>
              }
            </table>
          }
        </div>
      }
    } @else if (!error()) { <p class="sub">Loading…</p> }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    .tabs-row { display: flex; gap: 8px; margin-bottom: 14px; align-items: center; flex-wrap: wrap; }
  `,
})
export class ReceptionComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);

  readonly data = signal<Worklist | null>(null);
  readonly tab = signal<'res' | 'inf' | 'rep' | 'bal'>('res');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
    this.realtime.onArea('reception')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => void this.load());
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      this.data.set(await firstValueFrom(this.http.get<Worklist>(`${API_BASE_URL}/worklists/reception`)));
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async markInformed(item: PatientInformation): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.post(
        `${API_BASE_URL}/visits/${item.visitId}/samples/${item.sampleId}/mark-informed`, {}));
      this.info.set(`${item.patientName} informed about ${item.barcode} (${item.reasonCode}) — recorded in the audit trail.`);
      await this.load();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
