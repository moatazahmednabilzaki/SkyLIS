import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, inject, input, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { VisitsApi } from './visits.api';
import { API_BASE_URL } from '../../core/config';
import { PaymentResult, VisitDetails, problemMessage } from '../../core/api.types';

interface AttachmentRow {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAtUtc: string;
}

/** P05.3 Order Details + sample actions (collect / receive / reject) + payment capture. */
@Component({
  selector: 'app-visit-details',
  imports: [ReactiveFormsModule, DatePipe, DecimalPipe],
  template: `
    @if (visit(); as v) {
      <h1 class="pt">Visit {{ v.visitNumber }}
        <span class="chip c-blue">{{ v.status }}</span>
        @if (v.isStat) { <span class="chip c-red">STAT</span> }
        @if (v.status !== 'Cancelled' && v.status !== 'Reported') {
          <button class="btn sm danger" style="margin-left:10px" [disabled]="busy()" (click)="cancelVisit()">Cancel visit…</button>
        }
      </h1>
      <p class="sub">{{ v.patientName }} · registered {{ v.registeredAtUtc | date: 'yyyy-MM-dd HH:mm' }}</p>

      @if (error()) { <div class="err">{{ error() }}</div> }
      @if (info()) { <div class="note">{{ info() }}</div> }

      <div class="card">
        <h3>Samples</h3>
        <table class="t">
          <tr><th>Barcode</th><th>State</th><th>Condition</th><th>Actions</th></tr>
          @for (s of v.samples; track s.id) {
            <tr>
              <td class="mono">{{ s.barcode }}</td>
              <td>
                <span class="chip"
                      [class.c-green]="s.state === 'Received' || s.state === 'ReadyToCollect'"
                      [class.c-blue]="s.state === 'Collected'"
                      [class.c-amber]="s.state === 'ConditionPending'"
                      [class.c-red]="s.state === 'Rejected'">{{ s.state }}</span>
                @if (s.rejectionReasonCode) { <span class="hint">{{ s.rejectionReasonCode }}</span> }
              </td>
              <td>{{ s.condition ?? '—' }}
                @if (s.readyAtUtc) { <span class="hint">ready {{ s.readyAtUtc | date: 'HH:mm' }}</span> }
              </td>
              <td>
                @if (s.state === 'ReadyToCollect' || s.state === 'ConditionPending') {
                  <button class="btn sm" [disabled]="busy()" (click)="collect(s.id)">Collect ✓</button>
                }
                @if (s.state === 'Collected') {
                  <button class="btn sm green" [disabled]="busy()" (click)="receive(s.id)">Receive</button>
                  <button class="btn sm danger" [disabled]="busy()" (click)="reject(s.id)">Reject…</button>
                }
                @if (s.state === 'Received') {
                  <button class="btn sm danger" [disabled]="busy()" (click)="reject(s.id)">Reject…</button>
                }
              </td>
            </tr>
          }
        </table>
      </div>

      <div class="card">
        <h3>Tests</h3>
        <table class="t">
          <tr><th>Code</th><th>Status</th><th>Sample</th><th style="text-align:right">Price</th></tr>
          @for (t of v.tests; track t.id) {
            <tr>
              <td class="mono">{{ t.testCode }}</td>
              <td><span class="chip c-navy">{{ t.status }}</span></td>
              <td class="mono">{{ sampleBarcode(v, t.sampleId) }}</td>
              <td class="mono" style="text-align:right">{{ t.price }} {{ t.currency }}</td>
            </tr>
          }
        </table>
      </div>

      <div class="card">
        <h3>Capture payment (M17 — simplified)</h3>
        @if (payment(); as p) {
          <div class="note">Payment captured ✓ — invoice {{ p.status }},
            paid {{ p.paid }} / balance {{ p.balance }} {{ p.currency }}</div>
        }
        <form [formGroup]="paymentForm" (ngSubmit)="pay()">
          <div class="f-row">
            <div class="f">
              <label for="invoiceId">INVOICE ID</label>
              <input id="invoiceId" class="mono" formControlName="invoiceId">
            </div>
            <div class="f">
              <label for="amount">AMOUNT</label>
              <input id="amount" type="number" class="mono" formControlName="amount">
            </div>
            <div class="f">
              <label for="method">METHOD</label>
              <select id="method" formControlName="method">
                <option value="cash">Cash</option>
                <option value="card">Card</option>
                <option value="wallet">Wallet</option>
              </select>
            </div>
            <div class="f" style="flex:0 0 auto; align-self:flex-end">
              <button class="btn green" type="submit" [disabled]="paymentForm.invalid || busy()">Capture</button>
            </div>
          </div>
        </form>
      </div>
      <div class="card">
        <h3>Attachments (FR-SYS-007)</h3>
        <table class="t">
          <tr><th>File</th><th>Type</th><th>Size</th><th>Uploaded</th><th></th></tr>
          @for (a of attachments(); track a.id) {
            <tr>
              <td><b>{{ a.fileName }}</b></td>
              <td class="mono">{{ a.contentType }}</td>
              <td class="mono">{{ a.sizeBytes / 1024 | number: '1.0-1' }} KB</td>
              <td>{{ a.uploadedAtUtc | date: 'yyyy-MM-dd HH:mm' }}</td>
              <td><button class="btn sm ghost" (click)="download(a)">Download</button></td>
            </tr>
          }
          @if (attachments().length === 0) { <tr><td colspan="5" class="hint">No attachments.</td></tr> }
        </table>
        <div style="margin-top:8px">
          <input type="file" #filePicker (change)="upload(filePicker)">
        </div>
        <p class="hint">Requisition scans, instrument exports, consent forms — capped at 5 MB per file in Phase 1.</p>
      </div>
    } @else {
      <p class="sub">Loading visit…</p>
      @if (error()) { <div class="err">{{ error() }}</div> }
    }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class VisitDetailsComponent implements OnInit {
  private readonly api = inject(VisitsApi);
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  /** Route param bound via withComponentInputBinding (app.config). */
  readonly id = input.required<string>();

  readonly visit = signal<VisitDetails | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);
  readonly payment = signal<PaymentResult | null>(null);
  readonly attachments = signal<AttachmentRow[]>([]);

  readonly paymentForm = this.fb.nonNullable.group({
    invoiceId: ['', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    method: ['cash', Validators.required],
  });

  ngOnInit(): void {
    void this.reload();
  }

  async reload(): Promise<void> {
    try {
      this.visit.set(await firstValueFrom(this.api.get(this.id())));
      const params = new HttpParams().set('entityType', 'visit').set('entityId', this.id());
      this.attachments.set(await firstValueFrom(
        this.http.get<AttachmentRow[]>(`${API_BASE_URL}/attachments`, { params })));
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async cancelVisit(): Promise<void> {
    const reason = window.prompt('Cancellation reason (mandatory — the unpaid balance is waived by an automatic credit note):');
    if (!reason) return;
    await this.act(async () => {
      const result = await firstValueFrom(this.http.post<{ invoiceStatus: string; autoCreditNote: { creditNoteNumber: string } | null }>(
        `${API_BASE_URL}/visits/${this.id()}/cancel`, { reason }));
      this.info.set(result.autoCreditNote
        ? `Visit cancelled — credit note ${result.autoCreditNote.creditNoteNumber} waived the open balance (invoice ${result.invoiceStatus}).`
        : `Visit cancelled (invoice ${result.invoiceStatus}).`);
    });
  }

  async upload(picker: HTMLInputElement): Promise<void> {
    const file = picker.files?.[0];
    if (!file) return;
    const buffer = await file.arrayBuffer();
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.length; i += 32768) {
      binary += String.fromCharCode(...bytes.subarray(i, i + 32768));
    }
    await this.act(async () => {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/attachments`, {
        entityType: 'visit',
        entityId: this.id(),
        fileName: file.name,
        contentType: file.type || 'application/octet-stream',
        contentBase64: btoa(binary),
      }));
      picker.value = '';
      this.info.set(`Attached ${file.name} ✓`);
    });
  }

  download(attachment: AttachmentRow): void {
    void firstValueFrom(this.http.get(`${API_BASE_URL}/attachments/${attachment.id}/content`, { responseType: 'blob' }))
      .then(blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = attachment.fileName;
        link.click();
        URL.revokeObjectURL(url);
      });
  }

  sampleBarcode(visit: VisitDetails, sampleId: string): string {
    return visit.samples.find(s => s.id === sampleId)?.barcode ?? '?';
  }

  async collect(sampleId: string): Promise<void> {
    await this.act(() => firstValueFrom(this.api.collectSample(this.id(), sampleId)), 'Sample collected ✓');
  }

  async receive(sampleId: string): Promise<void> {
    await this.act(() => firstValueFrom(this.api.receiveSample(this.id(), sampleId)), 'Sample received & routed ✓');
  }

  async reject(sampleId: string): Promise<void> {
    const reason = window.prompt('Coded rejection reason (P03.4 vocabulary), e.g. HEMOLYZED:');
    if (!reason) return;
    await this.act(async () => {
      const result = await firstValueFrom(this.api.rejectSample(this.id(), sampleId, reason));
      this.info.set(`Sample rejected — recollection ${result.recollectionSampleId} issued; reception is notified to inform the patient (P07.3).`);
    });
  }

  async pay(): Promise<void> {
    const { invoiceId, amount, method } = this.paymentForm.getRawValue();
    const currency = this.visit()?.tests[0]?.currency ?? 'EGP';
    await this.act(async () => {
      this.payment.set(await firstValueFrom(this.api.capturePayment(invoiceId, amount, currency, method)));
    });
  }

  private async act(action: () => Promise<unknown>, successMessage?: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await action();
      if (successMessage) this.info.set(successMessage);
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
