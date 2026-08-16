import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { DuplicateCandidate, DuplicateGroup } from '../../core/api.types';
import { PatientsFacade } from './patients.facade';

@Component({
  selector: 'app-patients',
  imports: [ReactiveFormsModule, DatePipe, RouterLink],
  template: `
    <h1 class="pt">Patients</h1>
    <p class="sub">M04 · P04.1 — search by mobile number, part of name, national ID, or patient number.</p>

    <div class="card">
      <form (ngSubmit)="search()">
        <div class="f-row">
          <div class="f" style="flex:2">
            <label for="term">SEARCH</label>
            <input id="term" [formControl]="term" placeholder="Mobile · name · national ID · patient no.">
          </div>
          <div class="f" style="flex:0 0 auto; align-self:flex-end">
            <button class="btn" type="submit" [disabled]="term.invalid || facade.loading()">
              {{ facade.loading() ? 'Searching…' : 'Search' }}
            </button>
          </div>
        </div>
      </form>
      @if (facade.error()) { <div class="err">{{ facade.error() }}</div> }

      @if (facade.searched() && !facade.loading()) {
        @if (facade.results().length === 0) {
          <div class="note">No match — register the patient below; the record is saved once
            with a unique patient number and reused on every later visit.</div>
        } @else {
          <table class="t">
            <tr><th>Patient No.</th><th>Name</th><th>Mobile</th><th>Last visit</th><th>Age</th><th>Gender</th><th></th></tr>
            @for (p of facade.results(); track p.id) {
              <tr>
                <td class="mono">{{ p.patientNumber }}</td>
                <td><b>{{ p.fullName }}</b></td>
                <td class="mono">{{ p.mobileMasked }}</td>
                <td>{{ p.lastVisitAtUtc ? (p.lastVisitAtUtc | date: 'yyyy-MM-dd') : '—' }}</td>
                <td>{{ p.age }}</td>
                <td>{{ p.gender }}</td>
                <td><a class="btn sm ghost" [routerLink]="['/patients', p.id]">Patient 360 →</a></td>
              </tr>
            }
          </table>
          <p class="hint">Last visit date, age, and gender are the identity-confirmation triple (FR-PAT-001).</p>
        }
      }
    </div>

    <div class="card">
      <div style="display:flex; align-items:center; gap:10px">
        <h3 style="margin:0">Duplicate merge console (P04.4)</h3>
        <span style="flex:1"></span>
        <button class="btn ghost sm" (click)="scanDuplicates()">Scan for duplicates</button>
      </div>
      @if (scanned()) {
        @if (duplicates().length === 0) {
          <p class="hint" style="margin-top:8px">No duplicate candidates 🎉</p>
        } @else {
          @for (g of duplicates(); track g.matchedOn) {
            <p class="hint" style="margin:10px 0 4px">Matched on <b>{{ g.matchedOn }}</b></p>
            <table class="t">
              <tr><th>Patient No.</th><th>Name</th><th>Mobile</th><th>DOB</th><th>Visits</th><th></th></tr>
              @for (p of g.patients; track p.id) {
                <tr>
                  <td class="mono">{{ p.patientNumber }}</td>
                  <td><b>{{ p.fullName }}</b></td>
                  <td class="mono">{{ p.mobile }}</td>
                  <td>{{ p.dateOfBirth }}</td>
                  <td class="mono">{{ p.visitCount }}</td>
                  <td><button class="btn sm" (click)="keep(g, p)">Keep this record — merge the rest</button></td>
                </tr>
              }
            </table>
          }
        }
      }
    </div>

    <div class="card">
      <h3>＋ Register new patient (P04.2)</h3>
      @if (registered()) { <div class="note">Patient registered ✓ — id {{ registered() }}</div> }
      <form [formGroup]="form" (ngSubmit)="register()">
        <div class="f-row">
          <div class="f" style="flex:2">
            <label for="fullName">FULL NAME</label>
            <input id="fullName" formControlName="fullName">
          </div>
          <div class="f">
            <label for="sex">SEX</label>
            <select id="sex" formControlName="sex">
              <option value="Female">Female</option>
              <option value="Male">Male</option>
            </select>
          </div>
          <div class="f">
            <label for="dob">DATE OF BIRTH</label>
            <input id="dob" type="date" formControlName="dateOfBirth">
          </div>
        </div>
        <div class="f-row">
          <div class="f">
            <label for="mobile">MOBILE</label>
            <input id="mobile" class="mono" formControlName="mobile" placeholder="+20 100 234 5678">
          </div>
          <div class="f">
            <label for="nid">NATIONAL ID (OPTIONAL)</label>
            <input id="nid" class="mono" formControlName="nationalId">
          </div>
        </div>
        <button class="btn green" type="submit" [disabled]="form.invalid || busy()">
          {{ busy() ? 'Saving…' : 'Register patient' }}
        </button>
      </form>
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class PatientsComponent {
  readonly facade = inject(PatientsFacade);
  private readonly fb = inject(FormBuilder);

  readonly duplicates = this.facade.duplicates;
  readonly scanned = this.facade.scanned;

  readonly term = this.fb.nonNullable.control('', [Validators.required, Validators.minLength(2)]);
  readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required, Validators.minLength(3)]],
    sex: this.fb.nonNullable.control<'Female' | 'Male'>('Female'),
    dateOfBirth: ['', Validators.required],
    mobile: ['', [Validators.required, Validators.minLength(8)]],
    nationalId: [''],
  });
  readonly busy = signal(false);
  readonly registered = signal<string | null>(null);

  search(): void {
    if (this.term.valid) void this.facade.search(this.term.value);
  }

  scanDuplicates(): void {
    void this.facade.scanDuplicates();
  }

  keep(group: DuplicateGroup, survivor: DuplicateCandidate): void {
    const reason = window.prompt(
      `Merge ${group.patients.length - 1} duplicate(s) into ${survivor.patientNumber}? Reason (mandatory, audited):`);
    if (!reason) return;
    void this.facade.mergeGroupInto(group, survivor, reason);
  }

  async register(): Promise<void> {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.registered.set(null);
    try {
      const value = this.form.getRawValue();
      const id = await this.facade.register({
        fullName: value.fullName,
        sex: value.sex,
        dateOfBirth: value.dateOfBirth,
        mobile: value.mobile,
        nationalId: value.nationalId || null,
      });
      this.registered.set(id);
      this.form.reset({ sex: 'Female' });
    } catch {
      // error message is surfaced by the facade signal
    } finally {
      this.busy.set(false);
    }
  }
}
