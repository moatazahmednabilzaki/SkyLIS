import { DatePipe } from '@angular/common';
import { Component, inject, input, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { VisitsApi } from './visits.api';
import { PaymentResult, VisitDetails, problemMessage } from '../../core/api.types';

/** P05.3 Order Details + sample actions (collect / receive / reject) + payment capture. */
@Component({
  selector: 'app-visit-details',
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    @if (visit(); as v) {
      <h1 class="pt">Visit {{ v.visitNumber }}
        <span class="chip c-blue">{{ v.status }}</span>
        @if (v.isStat) { <span class="chip c-red">STAT</span> }
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
  private readonly fb = inject(FormBuilder);

  /** Route param bound via withComponentInputBinding (app.config). */
  readonly id = input.required<string>();

  readonly visit = signal<VisitDetails | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);
  readonly payment = signal<PaymentResult | null>(null);

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
    } catch (e) {
      this.error.set(problemMessage(e));
    }
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
