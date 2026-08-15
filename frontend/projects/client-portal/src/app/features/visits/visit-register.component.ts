import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PatientsApi } from '../patients/patients.api';
import { CatalogApi, OrgApi } from '../org/org.api';
import { VisitsApi } from './visits.api';
import { Branch, CatalogTest, PatientSearchResult, RegisteredVisit, problemMessage } from '../../core/api.types';

/**
 * P05.2 Visit Registration wizard: Patient → Branch & Tests → Confirm.
 * The specimen plan (condition consolidation + reservation) is computed server-side by
 * the SpecimenPlanner; this screen only captures intent and renders the result.
 */
@Component({
  selector: 'app-visit-register',
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    <h1 class="pt">Visit Registration</h1>
    <p class="sub">M05 · P05.2 — registration = the patient's new visit (SRS Rev 2.0 §M04).</p>

    <div class="steps">
      <span class="step" [class.cur]="step() === 1" [class.done]="step() > 1">1 · Patient</span>
      <span class="step" [class.cur]="step() === 2" [class.done]="step() > 2">2 · Branch &amp; Tests</span>
      <span class="step" [class.cur]="step() === 3">3 · Confirmed</span>
    </div>

    @if (error()) { <div class="err">{{ error() }}</div> }

    @if (step() === 1) {
      <div class="card">
        <h3>Find the patient</h3>
        <form (ngSubmit)="search()">
          <div class="f-row">
            <div class="f" style="flex:2">
              <label for="term">MOBILE · NAME · ID · PATIENT NO.</label>
              <input id="term" [formControl]="term">
            </div>
            <div class="f" style="flex:0 0 auto; align-self:flex-end">
              <button class="btn" type="submit" [disabled]="term.invalid || busy()">Search</button>
            </div>
          </div>
        </form>
        @if (results().length > 0) {
          <table class="t">
            <tr><th>Patient No.</th><th>Name</th><th>Last visit</th><th>Age</th><th>Gender</th><th></th></tr>
            @for (p of results(); track p.id) {
              <tr>
                <td class="mono">{{ p.patientNumber }}</td>
                <td><b>{{ p.fullName }}</b></td>
                <td>{{ p.lastVisitAtUtc ? (p.lastVisitAtUtc | date: 'yyyy-MM-dd') : '—' }}</td>
                <td>{{ p.age }}</td>
                <td>{{ p.gender }}</td>
                <td><button class="btn sm" (click)="choose(p)">Same patient — use record</button></td>
              </tr>
            }
          </table>
          <p class="hint">Confirm identity via last visit date, age, and gender before reusing the record.</p>
        }
      </div>
    }

    @if (step() === 2) {
      <div class="card">
        <h3>Patient: {{ patient()!.fullName }} <span class="chip c-blue mono">{{ patient()!.patientNumber }}</span></h3>

        <div class="f-row">
          <div class="f" style="flex:0 0 260px">
            <label for="branch">BRANCH (P03.2)</label>
            <select id="branch" [formControl]="branchId">
              @for (b of activeBranches(); track b.id) {
                <option [value]="b.id">{{ b.code }} — {{ b.name }}</option>
              }
            </select>
          </div>
          <div class="f" style="flex:0 0 auto">
            <label for="stat">PRIORITY</label>
            <select id="stat" [formControl]="isStat">
              <option [value]="false">Routine</option>
              <option [value]="true">STAT</option>
            </select>
          </div>
          @if (isStat.value === true || isStat.value === 'true') {
            <div class="f">
              <label for="statReason">STAT REASON (MANDATORY)</label>
              <input id="statReason" [formControl]="statReason">
            </div>
          }
        </div>

        <label class="lbl">TESTS (ACTIVE CATALOG)</label>
        @if (tests().length === 0) {
          <p class="hint">No active tests in the catalog yet — create and approve tests first (P03.3).</p>
        }
        <div class="picker">
          @for (t of tests(); track t.id) {
            <label class="pick" [class.sel]="selected().has(t.id)">
              <input type="checkbox" [checked]="selected().has(t.id)" (change)="toggleTest(t.id)">
              <span class="mono code">{{ t.code }}</span>
              <span class="tname">{{ t.name }}</span>
              <span class="price">{{ t.price }} {{ t.currency }}</span>
            </label>
          }
        </div>
        <p class="hint" style="margin-top:6px">
          {{ selected().size }} test(s) selected — total {{ selectedTotal() }} {{ tests()[0]?.currency ?? '' }}
        </p>

        <button class="btn ghost sm" (click)="step.set(1)">← Back</button>
        <button class="btn green" style="margin-left:8px"
                [disabled]="selected().size === 0 || !branchId.value || busy()" (click)="confirm()">
          {{ busy() ? 'Registering…' : 'Confirm visit — compute specimen plan' }}
        </button>
      </div>
    }

    @if (step() === 3 && registered(); as visit) {
      <div class="card">
        <h3>✅ Visit {{ visit.visitNumber }} registered</h3>
        <p class="hint" style="margin-bottom:10px">
          Invoice {{ visit.invoiceNumber }} issued — {{ visit.total }} {{ visit.currency }}.
        </p>
        <table class="t">
          <tr><th>Sample</th><th>State</th><th>Condition</th><th>Ready at (UTC)</th></tr>
          @for (s of visit.samples; track s.sampleId) {
            <tr>
              <td class="mono">{{ s.barcode }}</td>
              <td><span class="chip" [class.c-green]="s.state === 'ReadyToCollect'"
                        [class.c-amber]="s.state === 'ConditionPending'">{{ s.state }}</span></td>
              <td>{{ s.condition ?? '—' }}</td>
              <td class="mono">{{ s.readyAtUtc ? (s.readyAtUtc | date: 'HH:mm') : 'now' }}</td>
            </tr>
          }
        </table>
        <div style="margin-top:12px">
          <button class="btn" (click)="openVisit()">Open visit details →</button>
          <button class="btn ghost" style="margin-left:8px" (click)="reset()">New visit</button>
        </div>
      </div>
    }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    .steps { display: flex; gap: 8px; margin-bottom: 16px; }
    .step {
      font-size: 11.5px; color: var(--slate); background: #fff;
      border: 1px solid var(--line); border-radius: 20px; padding: 5px 14px;
    }
    .step.cur { color: #fff; background: var(--blue); border-color: var(--blue); font-weight: 700; }
    .step.done { color: var(--green); border-color: var(--green); }
    .lbl { display:block; font-size: 10px; font-weight: 700; letter-spacing: .1em; color: var(--slate); margin: 12px 0 6px; }
    .picker { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 6px; }
    .pick {
      display: flex; align-items: center; gap: 8px; border: 1px solid var(--line);
      border-radius: 8px; padding: 8px 10px; font-size: 12px; cursor: pointer; background: #fff;
    }
    .pick.sel { border-color: var(--blue); background: #f0f9ff; }
    .code { font-weight: 700; color: var(--blue); }
    .tname { flex: 1; }
    .price { color: var(--slate); font-size: 11px; }
  `,
})
export class VisitRegisterComponent implements OnInit {
  private readonly patientsApi = inject(PatientsApi);
  private readonly orgApi = inject(OrgApi);
  private readonly catalogApi = inject(CatalogApi);
  private readonly visitsApi = inject(VisitsApi);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly step = signal<1 | 2 | 3>(1);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly results = signal<PatientSearchResult[]>([]);
  readonly patient = signal<PatientSearchResult | null>(null);
  readonly registered = signal<RegisteredVisit | null>(null);
  readonly branches = signal<Branch[]>([]);
  readonly tests = signal<CatalogTest[]>([]);
  readonly selected = signal<Set<string>>(new Set());

  readonly activeBranches = computed(() => this.branches().filter(b => b.isActive));
  readonly selectedTotal = computed(() =>
    this.tests().filter(t => this.selected().has(t.id)).reduce((sum, t) => sum + (t.price ?? 0), 0));

  readonly term = this.fb.nonNullable.control('', [Validators.required, Validators.minLength(2)]);
  readonly branchId = this.fb.nonNullable.control('');
  readonly isStat = this.fb.nonNullable.control<boolean | string>(false);
  readonly statReason = this.fb.nonNullable.control('');

  ngOnInit(): void {
    void this.loadLookups();
  }

  private async loadLookups(): Promise<void> {
    try {
      const [branches, tests] = await Promise.all([
        firstValueFrom(this.orgApi.listBranches()),
        firstValueFrom(this.catalogApi.listTests('Active')),
      ]);
      this.branches.set(branches);
      this.tests.set(tests);
      const main = branches.find(b => b.isMain && b.isActive) ?? branches.find(b => b.isActive);
      if (main) this.branchId.setValue(main.id);
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async search(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.results.set(await firstValueFrom(this.patientsApi.search(this.term.value)));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  choose(patient: PatientSearchResult): void {
    this.patient.set(patient);
    this.step.set(2);
  }

  toggleTest(testId: string): void {
    this.selected.update(set => {
      const next = new Set(set);
      if (next.has(testId)) next.delete(testId);
      else next.add(testId);
      return next;
    });
  }

  async confirm(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const stat = this.isStat.value === true || this.isStat.value === 'true';
    try {
      const visit = await firstValueFrom(this.visitsApi.register({
        patientId: this.patient()!.id,
        branchId: this.branchId.value,
        testIds: [...this.selected()],
        isStat: stat,
        statReason: stat ? this.statReason.value : null,
      }));
      this.registered.set(visit);
      this.step.set(3);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  openVisit(): void {
    void this.router.navigate(['/visits', this.registered()!.visitId]);
  }

  reset(): void {
    this.step.set(1);
    this.patient.set(null);
    this.registered.set(null);
    this.results.set([]);
    this.selected.set(new Set());
    this.term.reset();
  }
}
