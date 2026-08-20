import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { CatalogApi } from './org.api';
import { CatalogSampleType, CatalogTest, problemMessage } from '../../core/api.types';

/**
 * P03.3 Test Catalogue: create a tenant test, walk it through review (Draft → InReview →
 * Active), activate platform-pushed tests, and define result schemas. Until at least one
 * test is Active, visits cannot be registered — this page is the on-ramp for a fresh lab.
 */
@Component({
  selector: 'app-catalog',
  imports: [ReactiveFormsModule],
  template: `
    <h1 class="pt">Test Catalogue</h1>
    <p class="sub">M03 · P03.3 — a test must reach <b>Active</b> before it can be ordered on a visit.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note">{{ info() }}</div> }

    @if (activeCount() === 0) {
      <div class="note" style="border-color:var(--amber)">
        No active tests yet — create one below, then <b>Submit</b> and <b>Approve</b> it.
        Only then does it appear in the visit-registration test picker.
      </div>
    }

    <div class="card">
      <h3>＋ Add a tenant test (P03.3)</h3>
      <form [formGroup]="form" (ngSubmit)="create()">
        <div class="f-row">
          <div class="f" style="flex:0 0 120px">
            <label for="code">CODE</label>
            <input id="code" class="mono" formControlName="code" placeholder="GLU-F">
          </div>
          <div class="f" style="flex:2">
            <label for="name">NAME</label>
            <input id="name" formControlName="name" placeholder="Fasting Glucose">
          </div>
          <div class="f">
            <label for="dept">DEPARTMENT</label>
            <input id="dept" formControlName="department" placeholder="Chemistry">
          </div>
        </div>
        <div class="f-row">
          <div class="f" style="flex:2">
            <label for="st">SAMPLE TYPE</label>
            <select id="st" formControlName="sampleTypeId" (change)="onSampleTypeChange()">
              <option value="">— select —</option>
              @for (s of sampleTypes(); track s.id) {
                <option [value]="s.id">{{ s.name }} ({{ s.containerName }})</option>
              }
            </select>
          </div>
          <div class="f" style="flex:2">
            <label for="cond">REQUIRED CONDITION (OPTIONAL)</label>
            <select id="cond" formControlName="requiredConditionId">
              <option [value]="null">— none —</option>
              @for (c of conditionsForSelected(); track c.id) {
                <option [value]="c.id">{{ c.name }}{{ c.delayMinutes ? ' (+' + c.delayMinutes + 'min)' : '' }}</option>
              }
            </select>
          </div>
          <div class="f" style="flex:0 0 120px">
            <label for="price">PRICE</label>
            <input id="price" type="number" step="0.01" formControlName="price">
          </div>
          <div class="f" style="flex:0 0 90px">
            <label for="cur">CURRENCY</label>
            <input id="cur" class="mono" formControlName="currency">
          </div>
        </div>
        <button class="btn green" type="submit" [disabled]="form.invalid || busy()">
          {{ busy() ? 'Saving…' : 'Create test (Draft)' }}
        </button>
      </form>
    </div>

    <div class="card">
      <h3>Catalogue ({{ tests().length }})</h3>
      <table class="t">
        <tr><th>Code</th><th>Name</th><th>Dept</th><th>Status</th><th>Price</th><th>Schema</th><th>Actions</th></tr>
        @for (t of tests(); track t.id) {
          <tr>
            <td class="mono"><b>{{ t.code }}</b></td>
            <td>{{ t.name }}</td>
            <td>{{ t.department }}</td>
            <td>
              <span class="chip"
                    [class.c-green]="t.status === 'Active'"
                    [class.c-amber]="t.status === 'InReview' || t.status === 'PendingActivation'"
                    [class.c-blue]="t.status === 'Draft'">{{ t.status }}</span>
            </td>
            <td class="mono">{{ t.price !== null ? t.price + ' ' + t.currency : '—' }}</td>
            <td>{{ t.hasResultSchema ? '✅' : '⬜' }}</td>
            <td>
              @if (t.status === 'Draft') {
                <button class="btn sm" (click)="submit(t)" [disabled]="busy()">Submit for review</button>
              }
              @if (t.status === 'InReview') {
                <button class="btn sm green" (click)="approve(t)" [disabled]="busy()">Approve → activate</button>
              }
              @if (t.status === 'PendingActivation') {
                <button class="btn sm green" (click)="activatePushed(t)" [disabled]="busy()">Activate (set price)</button>
              }
              @if (t.status === 'Active') {
                <button class="btn sm ghost" (click)="openSchema(t)">
                  {{ t.hasResultSchema ? 'Edit schema' : 'Set result schema' }}
                </button>
              }
            </td>
          </tr>
        }
        @if (tests().length === 0) {
          <tr><td colspan="7" class="hint">No tests yet.</td></tr>
        }
      </table>
    </div>

    @if (schemaFor(); as t) {
      <div class="card" style="border-color:var(--blue)">
        <h3>Result schema — <span class="mono">{{ t.code }}</span> {{ t.name }}</h3>
        <p class="hint" style="margin-bottom:8px">Required before results can be entered (M09). Reference range flags Low/High; critical range never auto-verifies.</p>
        <form [formGroup]="schemaForm" (ngSubmit)="saveSchema(t)">
          <div class="f-row">
            <div class="f" style="flex:0 0 100px"><label>UNIT</label><input class="mono" formControlName="unit" placeholder="mg/dL"></div>
            <div class="f"><label>REF LOW</label><input type="number" step="0.01" formControlName="refLow"></div>
            <div class="f"><label>REF HIGH</label><input type="number" step="0.01" formControlName="refHigh"></div>
            <div class="f"><label>CRITICAL LOW</label><input type="number" step="0.01" formControlName="criticalLow"></div>
            <div class="f"><label>CRITICAL HIGH</label><input type="number" step="0.01" formControlName="criticalHigh"></div>
          </div>
          <div class="f-row">
            <div class="f"><label>ABSURD LOW</label><input type="number" step="0.01" formControlName="absurdLow"></div>
            <div class="f"><label>ABSURD HIGH</label><input type="number" step="0.01" formControlName="absurdHigh"></div>
            <div class="f"><label>DELTA %</label><input type="number" step="0.01" formControlName="deltaThresholdPercent"></div>
            <div class="f" style="align-self:flex-end">
              <label><input type="checkbox" formControlName="autoVerify"> Auto-verify clean results</label>
            </div>
          </div>
          <button class="btn green" type="submit" [disabled]="schemaForm.invalid || busy()">Save schema</button>
          <button class="btn ghost" type="button" style="margin-left:8px" (click)="schemaFor.set(null)">Cancel</button>
        </form>
      </div>
    }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class CatalogComponent implements OnInit {
  private readonly api = inject(CatalogApi);
  private readonly fb = inject(FormBuilder);

  readonly tests = signal<CatalogTest[]>([]);
  readonly sampleTypes = signal<CatalogSampleType[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);
  readonly schemaFor = signal<CatalogTest | null>(null);

  readonly activeCount = computed(() => this.tests().filter(t => t.status === 'Active').length);
  readonly conditionsForSelected = computed(() =>
    this.sampleTypes().find(s => s.id === this.form.controls.sampleTypeId.value)?.conditions ?? []);

  readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9-]+$/)]],
    name: ['', Validators.required],
    department: ['', Validators.required],
    sampleTypeId: ['', Validators.required],
    requiredConditionId: this.fb.control<string | null>(null),
    price: [0, [Validators.required, Validators.min(0.01)]],
    currency: ['EGP', [Validators.required, Validators.minLength(3), Validators.maxLength(3)]],
  });

  readonly schemaForm = this.fb.nonNullable.group({
    unit: ['', Validators.required],
    refLow: this.fb.control<number | null>(null),
    refHigh: this.fb.control<number | null>(null),
    criticalLow: this.fb.control<number | null>(null),
    criticalHigh: this.fb.control<number | null>(null),
    absurdLow: this.fb.control<number | null>(null),
    absurdHigh: this.fb.control<number | null>(null),
    autoVerify: [false],
    deltaThresholdPercent: this.fb.control<number | null>(null),
  });

  ngOnInit(): void {
    void this.reload();
  }

  async reload(): Promise<void> {
    this.error.set(null);
    try {
      const [tests, sampleTypes] = await Promise.all([
        firstValueFrom(this.api.listTests()),
        firstValueFrom(this.api.listSampleTypes()),
      ]);
      this.tests.set(tests);
      this.sampleTypes.set(sampleTypes);
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  onSampleTypeChange(): void {
    this.form.controls.requiredConditionId.setValue(null);
  }

  async create(): Promise<void> {
    if (this.form.invalid) return;
    await this.run(async () => {
      const v = this.form.getRawValue();
      await firstValueFrom(this.api.createTest({
        code: v.code, name: v.name, department: v.department, sampleTypeId: v.sampleTypeId,
        requiredConditionId: v.requiredConditionId || null, price: v.price, currency: v.currency,
      }));
      this.info.set(`Test ${v.code} created as Draft — submit and approve it to make it orderable.`);
      this.form.reset({ currency: 'EGP', price: 0, requiredConditionId: null });
    });
  }

  submit(t: CatalogTest): Promise<void> {
    return this.run(async () => {
      await firstValueFrom(this.api.submitTest(t.id));
      this.info.set(`${t.code} submitted for review.`);
    });
  }

  approve(t: CatalogTest): Promise<void> {
    return this.run(async () => {
      await firstValueFrom(this.api.approveTest(t.id));
      this.info.set(`${t.code} approved and activated — it is now orderable on visits.`);
    });
  }

  activatePushed(t: CatalogTest): Promise<void> {
    const price = Number(window.prompt(`Set the local price for ${t.code} (${t.currency || 'EGP'}):`, '0'));
    if (!price || price <= 0) return Promise.resolve();
    return this.run(async () => {
      await firstValueFrom(this.api.activatePushedTest(t.id, price, t.currency || 'EGP'));
      this.info.set(`${t.code} activated at ${price}.`);
    });
  }

  openSchema(t: CatalogTest): void {
    this.schemaForm.reset({ unit: '', autoVerify: false });
    this.schemaFor.set(t);
  }

  async saveSchema(t: CatalogTest): Promise<void> {
    if (this.schemaForm.invalid) return;
    await this.run(async () => {
      await firstValueFrom(this.api.setResultSchema(t.id, this.schemaForm.getRawValue()));
      this.info.set(`Result schema saved for ${t.code}.`);
      this.schemaFor.set(null);
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await action();
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
