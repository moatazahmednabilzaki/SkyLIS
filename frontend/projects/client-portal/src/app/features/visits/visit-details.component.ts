import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { VisitsApi } from './visits.api';
import { API_BASE_URL } from '../../core/config';
import { InvoiceDetails, VisitDetails, problemMessage } from '../../core/api.types';

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
  imports: [FormsModule, DatePipe, DecimalPipe],
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
        <h3>Billing (M17)</h3>
        @if (invoice(); as inv) {
          <table class="t" style="max-width:460px">
            <tr><td>Invoice</td><td class="mono"><b>{{ inv.invoiceNumber }}</b>
              <span class="chip" [class.c-green]="inv.status === 'Paid'"
                    [class.c-amber]="inv.status === 'PartiallyPaid'"
                    [class.c-navy]="inv.status === 'Adjusted'">{{ inv.status }}</span></td></tr>
            <tr><td>Total</td><td class="mono">{{ inv.total }} {{ inv.currency }}</td></tr>
            @if (inv.discountAmount > 0) {
              <tr><td>Discount</td><td class="mono">−{{ inv.discountAmount }} <span class="hint">{{ inv.discountReason }}</span></td></tr>
            }
            @if (inv.creditedAmount > 0) {
              <tr><td>Credited</td><td class="mono">−{{ inv.creditedAmount }}</td></tr>
            }
            <tr><td>Paid</td><td class="mono">{{ inv.paid }}@if (inv.refunded > 0) { <span class="hint"> (refunded {{ inv.refunded }})</span> }</td></tr>
            <tr><td><b>Balance</b></td><td class="mono"><b [style.color]="inv.balance > 0 ? 'var(--red)' : 'var(--green)'">{{ inv.balance }} {{ inv.currency }}</b></td></tr>
          </table>

          <label class="lbl">Capture payment (P17.1)</label>
          <div class="f-row">
            <div class="f" style="flex:0 0 130px"><label for="bill-pay">AMOUNT</label><input id="bill-pay" type="number" class="mono" [(ngModel)]="payAmount"></div>
            <div class="f" style="flex:0 0 auto"><label for="bill-method">METHOD</label>
              <select id="bill-method" [(ngModel)]="payMethod"><option value="cash">Cash</option><option value="card">Card</option><option value="wallet">Wallet</option></select></div>
            <div class="f" style="flex:0 0 auto; align-self:flex-end"><button class="btn green" [disabled]="busy()" (click)="capture(inv)">Capture</button></div>
          </div>

          <label class="lbl">Discount (before payment)</label>
          <div class="f-row">
            <div class="f" style="flex:0 0 130px"><label for="bill-discount">AMOUNT</label><input id="bill-discount" type="number" class="mono" [(ngModel)]="discountAmount"></div>
            <div class="f"><label for="bill-discount-reason">REASON (MANDATORY)</label><input id="bill-discount-reason" [(ngModel)]="discountReason"></div>
            <div class="f" style="flex:0 0 auto; align-self:flex-end"><button class="btn" [disabled]="busy()" (click)="applyDiscount(inv)">Apply discount</button></div>
          </div>

          <label class="lbl">Credit note (waive balance)</label>
          <div class="f-row">
            <div class="f" style="flex:0 0 130px"><label for="bill-credit">AMOUNT</label><input id="bill-credit" type="number" class="mono" [(ngModel)]="creditAmount"></div>
            <div class="f"><label for="bill-credit-reason">REASON (MANDATORY)</label><input id="bill-credit-reason" [(ngModel)]="creditReason"></div>
            <div class="f" style="flex:0 0 auto; align-self:flex-end"><button class="btn" [disabled]="busy()" (click)="issueCredit(inv)">Issue credit note</button></div>
          </div>

          <label class="lbl">Refund (returns captured money — SoD)</label>
          <div class="f-row">
            <div class="f" style="flex:0 0 130px"><label for="bill-refund">AMOUNT</label><input id="bill-refund" type="number" class="mono" [(ngModel)]="refundAmount"></div>
            <div class="f"><label for="bill-refund-reason">REASON (MANDATORY)</label><input id="bill-refund-reason" [(ngModel)]="refundReason"></div>
            <div class="f" style="flex:0 0 auto; align-self:flex-end"><button class="btn danger" [disabled]="busy()" (click)="refund(inv)">Refund</button></div>
          </div>

          @if (inv.creditNotes.length > 0) {
            <label class="lbl">Credit notes</label>
            <table class="t" style="max-width:520px">
              <tr><th>Number</th><th>Amount</th><th>Reason</th></tr>
              @for (c of inv.creditNotes; track c.id) {
                <tr><td class="mono">{{ c.creditNoteNumber }}</td><td class="mono">{{ c.amount }} {{ c.currency }}</td><td>{{ c.reason }}</td></tr>
              }
            </table>
          }
        } @else { <p class="hint">No invoice for this visit.</p> }
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
    .lbl { display:block; font-size: 10px; font-weight: 700; letter-spacing: .1em; color: var(--slate); margin: 14px 0 6px; }
  `,
})
export class VisitDetailsComponent implements OnInit {
  private readonly api = inject(VisitsApi);
  private readonly http = inject(HttpClient);

  /** Route param bound via withComponentInputBinding (app.config). */
  readonly id = input.required<string>();

  readonly visit = signal<VisitDetails | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);
  readonly invoice = signal<InvoiceDetails | null>(null);
  readonly attachments = signal<AttachmentRow[]>([]);

  // Billing panel inputs (M17 edge paths).
  payAmount = 0;
  payMethod = 'cash';
  discountAmount = 0;
  discountReason = '';
  creditAmount = 0;
  creditReason = '';
  refundAmount = 0;
  refundReason = '';

  ngOnInit(): void {
    void this.reload();
  }

  async reload(): Promise<void> {
    try {
      this.visit.set(await firstValueFrom(this.api.get(this.id())));
      const params = new HttpParams().set('entityType', 'visit').set('entityId', this.id());
      this.attachments.set(await firstValueFrom(
        this.http.get<AttachmentRow[]>(`${API_BASE_URL}/attachments`, { params })));
      this.invoice.set(await firstValueFrom(
        this.http.get<InvoiceDetails>(`${API_BASE_URL}/billing/invoices/by-visit/${this.id()}`)));
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

  async capture(inv: InvoiceDetails): Promise<void> {
    await this.act(async () => {
      await firstValueFrom(this.api.capturePayment(inv.id, this.payAmount, inv.currency, this.payMethod));
      this.payAmount = 0;
      this.info.set('Payment captured ✓');
    });
  }

  async applyDiscount(inv: InvoiceDetails): Promise<void> {
    await this.act(async () => {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/billing/invoices/${inv.id}/discount`,
        { amount: this.discountAmount, reason: this.discountReason }));
      this.discountAmount = 0; this.discountReason = '';
      this.info.set('Discount applied ✓');
    });
  }

  async issueCredit(inv: InvoiceDetails): Promise<void> {
    await this.act(async () => {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/billing/invoices/${inv.id}/credit-notes`,
        { amount: this.creditAmount, reason: this.creditReason }));
      this.creditAmount = 0; this.creditReason = '';
      this.info.set('Credit note issued ✓');
    });
  }

  async refund(inv: InvoiceDetails): Promise<void> {
    await this.act(async () => {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/billing/invoices/${inv.id}/refunds`,
        { amount: this.refundAmount, reason: this.refundReason }));
      this.refundAmount = 0; this.refundReason = '';
      this.info.set('Refund processed ✓');
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
