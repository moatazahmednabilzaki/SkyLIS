import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/auth.service';

interface MasterTest {
  id: string;
  code: string;
  name: string;
  department: string;
  sampleTypeName: string;
  containerName: string;
  conditionName: string | null;
  createdAtUtc: string;
  lastPushedAtUtc: string | null;
  pushCount: number;
}

/**
 * P01.7 Master Data Packs: the platform test catalogue. Pushing (FR-MDM-071) fans one
 * reliable event out per tenant; tests arrive there as PENDING ACTIVATION — every tenant
 * must set its own price before the test becomes orderable.
 */
@Component({
  selector: 'app-master-data',
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    <h1 class="pt">Master Data Packs</h1>
    <p class="sub">M01 · P01.7 — platform test catalogue with push-to-all-tenants (FR-MDM-071).</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note">{{ info() }}</div> }

    <div class="card">
      <h3>Add a master test</h3>
      <form (ngSubmit)="create()">
        <div class="f-row">
          <div class="f" style="flex:0 0 110px">
            <label for="code">CODE</label>
            <input id="code" class="mono" [formControl]="code" placeholder="CBC">
          </div>
          <div class="f" style="flex:2">
            <label for="name">NAME</label>
            <input id="name" [formControl]="name" placeholder="Complete Blood Count">
          </div>
          <div class="f">
            <label for="dept">DEPARTMENT</label>
            <input id="dept" [formControl]="department" placeholder="Hematology">
          </div>
        </div>
        <div class="f-row">
          <div class="f">
            <label for="st">SAMPLE TYPE (BY NAME)</label>
            <input id="st" [formControl]="sampleTypeName" placeholder="Whole blood (EDTA)">
          </div>
          <div class="f">
            <label for="ct">CONTAINER</label>
            <input id="ct" [formControl]="containerName" placeholder="EDTA (lavender)">
          </div>
          <div class="f">
            <label for="cond">CONDITION (OPTIONAL)</label>
            <input id="cond" [formControl]="conditionName" placeholder="Random">
          </div>
          <div class="f" style="flex:0 0 auto; align-self:flex-end">
            <button class="btn" type="submit"
                    [disabled]="code.invalid || name.invalid || department.invalid || sampleTypeName.invalid || containerName.invalid || busy()">
              Add to catalogue
            </button>
          </div>
        </div>
      </form>
    </div>

    <div class="card">
      <h3>Platform catalogue</h3>
      <table class="t">
        <tr><th>Code</th><th>Name</th><th>Department</th><th>Sample type</th><th>Condition</th><th>Pushed</th><th></th></tr>
        @for (t of tests(); track t.id) {
          <tr>
            <td class="mono"><b>{{ t.code }}</b></td>
            <td>{{ t.name }}</td>
            <td>{{ t.department }}</td>
            <td>{{ t.sampleTypeName }} <span class="hint">({{ t.containerName }})</span></td>
            <td>{{ t.conditionName ?? '—' }}</td>
            <td>
              @if (t.lastPushedAtUtc) {
                <span class="chip c-green">{{ t.pushCount }} tenant(s)</span>
                <span class="hint">{{ t.lastPushedAtUtc | date: 'MM-dd HH:mm' }}</span>
              } @else { <span class="chip">never</span> }
            </td>
            <td><button class="btn sm" (click)="push(t)" [disabled]="busy()">Push to all tenants</button></td>
          </tr>
        }
        @if (tests().length === 0) { <tr><td colspan="7" class="hint">The platform catalogue is empty.</td></tr> }
      </table>
      <p class="hint">Pushed tests arrive as PENDING ACTIVATION — each tenant sets its own price to activate (price gate).</p>
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: #fff; margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class MasterDataComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  readonly tests = signal<MasterTest[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);

  readonly code = this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(20)]);
  readonly name = this.fb.nonNullable.control('', Validators.required);
  readonly department = this.fb.nonNullable.control('', Validators.required);
  readonly sampleTypeName = this.fb.nonNullable.control('', Validators.required);
  readonly containerName = this.fb.nonNullable.control('', Validators.required);
  readonly conditionName = this.fb.nonNullable.control('');

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      this.tests.set(await firstValueFrom(
        this.http.get<MasterTest[]>(`${API_BASE_URL}/platform/master-tests`)));
    } catch {
      this.error.set('Could not load the master catalogue. Is the API running?');
    }
  }

  async create(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/platform/master-tests`, {
        code: this.code.value, name: this.name.value, department: this.department.value,
        sampleTypeName: this.sampleTypeName.value, containerName: this.containerName.value,
        conditionName: this.conditionName.value || null,
      }));
      this.code.reset(); this.name.reset(); this.department.reset();
      this.sampleTypeName.reset(); this.containerName.reset(); this.conditionName.reset();
      await this.load();
    } catch {
      this.error.set('Creating the master test failed (duplicate code?).');
    } finally {
      this.busy.set(false);
    }
  }

  async push(test: MasterTest): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      const result = await firstValueFrom(this.http.post<{ targetCount: number }>(
        `${API_BASE_URL}/platform/master-tests/${test.id}/push`, {}));
      this.info.set(`${test.code} queued for ${result.targetCount} tenant(s) — delivered via the reliable outbox.`);
      await this.load();
    } catch {
      this.error.set('Push failed.');
    } finally {
      this.busy.set(false);
    }
  }
}
