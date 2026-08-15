import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PatientsApi } from '../patients/patients.api';
import { VisitsApi } from './visits.api';
import { PatientSearchResult, RegisteredVisit, problemMessage } from '../../core/api.types';

/**
 * P05.2 Visit Registration wizard: Patient → Tests → Confirm.
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
      <span class="step" [class.cur]="step() === 2" [class.done]="step() > 2">2 · Tests</span>
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
          <div class="f" style="flex:2">
            <label for="tests">TEST IDS (comma-separated GUIDs — the test-picker UI arrives with the catalog slice)</label>
            <input id="tests" class="mono" [formControl]="testIds"
                   placeholder="guid-1, guid-2">
          </div>
        </div>
        <div class="f-row">
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
        <button class="btn ghost sm" (click)="step.set(1)">← Back</button>
        <button class="btn green" style="margin-left:8px" [disabled]="testIds.invalid || busy()" (click)="confirm()">
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
  `,
})
export class VisitRegisterComponent {
  private readonly patientsApi = inject(PatientsApi);
  private readonly visitsApi = inject(VisitsApi);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly step = signal<1 | 2 | 3>(1);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly results = signal<PatientSearchResult[]>([]);
  readonly patient = signal<PatientSearchResult | null>(null);
  readonly registered = signal<RegisteredVisit | null>(null);

  readonly term = this.fb.nonNullable.control('', [Validators.required, Validators.minLength(2)]);
  readonly testIds = this.fb.nonNullable.control('', Validators.required);
  readonly isStat = this.fb.nonNullable.control<boolean | string>(false);
  readonly statReason = this.fb.nonNullable.control('');

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

  async confirm(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const stat = this.isStat.value === true || this.isStat.value === 'true';
    try {
      const visit = await firstValueFrom(this.visitsApi.register({
        patientId: this.patient()!.id,
        testIds: this.testIds.value.split(',').map(id => id.trim()).filter(id => id.length > 0),
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
    this.term.reset();
    this.testIds.reset();
  }
}
