import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { OrgApi } from './org.api';
import { Branch, problemMessage } from '../../core/api.types';

/**
 * P03.2 Branches & Departments: physical locations of the lab. The MAIN branch ships
 * with provisioning; visit/invoice numbers embed the branch code (V-MAIN-…).
 */
@Component({
  selector: 'app-branches',
  imports: [ReactiveFormsModule, FormsModule],
  template: `
    <h1 class="pt">Branches &amp; Departments</h1>
    <p class="sub">M03 · P03.2 — visits, invoices, and number series run per branch.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }

    <div class="card">
      <h3>Open a new branch</h3>
      <form (ngSubmit)="create()">
        <div class="f-row">
          <div class="f" style="flex:0 0 130px">
            <label for="code">CODE (2–10 A–Z0–9)</label>
            <input id="code" class="mono" [formControl]="code" placeholder="ZMLK" maxlength="10">
          </div>
          <div class="f" style="flex:2">
            <label for="name">NAME</label>
            <input id="name" [formControl]="name" placeholder="Zamalek Branch">
          </div>
          <div class="f" style="flex:2">
            <label for="address">ADDRESS (OPTIONAL)</label>
            <input id="address" [formControl]="address">
          </div>
          <div class="f" style="flex:1">
            <label for="phone">PHONE (OPTIONAL)</label>
            <input id="phone" [formControl]="phone">
          </div>
          <div class="f" style="flex:0 0 auto; align-self:flex-end">
            <button class="btn" type="submit" [disabled]="code.invalid || name.invalid || busy()">Open branch</button>
          </div>
        </div>
      </form>
    </div>

    @for (b of branches(); track b.id) {
      <div class="card">
        <div style="display:flex; align-items:center; gap:10px">
          <h3 style="margin:0">{{ b.name }}</h3>
          <span class="chip c-blue mono">{{ b.code }}</span>
          @if (b.isMain) { <span class="chip c-green">MAIN</span> }
          @if (!b.isActive) { <span class="chip c-red">DEACTIVATED</span> }
          <span class="spacer" style="flex:1"></span>
          @if (!b.isMain) {
            <button class="btn ghost sm" (click)="toggle(b)">
              {{ b.isActive ? 'Deactivate' : 'Reactivate' }}
            </button>
          }
        </div>
        @if (b.address || b.phone) {
          <p class="hint" style="margin:6px 0 0">{{ b.address ?? '' }} {{ b.phone ? '· ' + b.phone : '' }}</p>
        }

        <table class="t" style="margin-top:10px">
          <tr><th>Department</th><th>Code</th></tr>
          @for (d of b.departments; track d.id) {
            <tr><td>{{ d.name }}</td><td class="mono">{{ d.code }}</td></tr>
          }
          @if (b.departments.length === 0) {
            <tr><td colspan="2" class="hint">No departments yet.</td></tr>
          }
        </table>

        <form style="margin-top:8px" (ngSubmit)="addDept(b)">
          <div class="f-row">
            <div class="f" style="flex:0 0 130px">
              <label [for]="'dc-' + b.id">DEPT CODE</label>
              <input [id]="'dc-' + b.id" class="mono" maxlength="10"
                     [value]="deptCode()[b.id] ?? ''" (input)="setDeptCode(b.id, $event)">
            </div>
            <div class="f" style="flex:2">
              <label [for]="'dn-' + b.id">DEPT NAME</label>
              <input [id]="'dn-' + b.id"
                     [value]="deptName()[b.id] ?? ''" (input)="setDeptName(b.id, $event)">
            </div>
            <div class="f" style="flex:0 0 auto; align-self:flex-end">
              <button class="btn sm" type="submit" [disabled]="busy()">Add department</button>
            </div>
          </div>
        </form>
      </div>
    }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class BranchesComponent implements OnInit {
  private readonly api = inject(OrgApi);
  private readonly fb = inject(FormBuilder);

  readonly branches = signal<Branch[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly deptCode = signal<Record<string, string>>({});
  readonly deptName = signal<Record<string, string>>({});

  readonly code = this.fb.nonNullable.control('', [Validators.required, Validators.pattern(/^[A-Za-z0-9]{2,10}$/)]);
  readonly name = this.fb.nonNullable.control('', Validators.required);
  readonly address = this.fb.nonNullable.control('');
  readonly phone = this.fb.nonNullable.control('');

  ngOnInit(): void {
    void this.reload();
  }

  async reload(): Promise<void> {
    try {
      this.branches.set(await firstValueFrom(this.api.listBranches()));
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async create(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.createBranch(
        this.code.value, this.name.value,
        this.address.value || null, this.phone.value || null));
      this.code.reset(); this.name.reset(); this.address.reset(); this.phone.reset();
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  setDeptCode(branchId: string, event: Event): void {
    this.deptCode.update(m => ({ ...m, [branchId]: (event.target as HTMLInputElement).value }));
  }

  setDeptName(branchId: string, event: Event): void {
    this.deptName.update(m => ({ ...m, [branchId]: (event.target as HTMLInputElement).value }));
  }

  async addDept(branch: Branch): Promise<void> {
    const code = this.deptCode()[branch.id]?.trim();
    const name = this.deptName()[branch.id]?.trim();
    if (!code || !name) { this.error.set('Department code and name are required.'); return; }
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.addDepartment(branch.id, code, name));
      this.deptCode.update(m => ({ ...m, [branch.id]: '' }));
      this.deptName.update(m => ({ ...m, [branch.id]: '' }));
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  async toggle(branch: Branch): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.setBranchActive(branch.id, !branch.isActive));
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
