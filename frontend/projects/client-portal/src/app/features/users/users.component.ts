import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { problemMessage } from '../../core/api.types';

interface UserRow {
  id: string;
  userName: string;
  fullName: string;
  roles: string[];
  status: string;
  lastLoginAtUtc: string | null;
}

interface RoleInfo { role: string; permissions: string[]; }

/** M02 · P02.1/P02.2 — users directory with system-role assignment. */
@Component({
  selector: 'app-users',
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    <h1 class="pt">Users &amp; Roles</h1>
    <p class="sub">M02 · P02.1 — personal accounts with system-role permission bundles. Monitored read-only by the platform (P01.5).</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note">{{ info() }}</div> }

    <div class="card">
      <h3>Users</h3>
      <table class="t">
        <tr><th>Username</th><th>Full name</th><th>Roles</th><th>Status</th><th>Last login</th><th>Actions</th></tr>
        @for (u of users(); track u.id) {
          <tr>
            <td class="mono">{{ u.userName }}</td>
            <td><b>{{ u.fullName }}</b></td>
            <td>@for (r of u.roles; track r) { <span class="chip c-blue">{{ r }}</span> }</td>
            <td><span class="chip" [class.c-green]="u.status === 'Active'"
                  [class.c-red]="u.status !== 'Active'">{{ u.status }}</span></td>
            <td>{{ u.lastLoginAtUtc ? (u.lastLoginAtUtc | date: 'MM-dd HH:mm') : '—' }}</td>
            <td>
              @if (u.status === 'Active') {
                <button class="btn sm ghost" [disabled]="busy()" (click)="setStatus(u, 'lock')">Lock</button>
              }
              @if (u.status === 'Locked') {
                <button class="btn sm" [disabled]="busy()" (click)="setStatus(u, 'unlock')">Unlock</button>
              }
              @if (u.status !== 'Deactivated') {
                <button class="btn sm danger" [disabled]="busy()" (click)="setStatus(u, 'deactivate')">Deactivate</button>
                <button class="btn sm ghost" [disabled]="busy()" (click)="resetPassword(u)">Reset password…</button>
              }
            </td>
          </tr>
        }
      </table>
    </div>

    <div class="card">
      <h3>＋ Create user</h3>
      <form [formGroup]="form" (ngSubmit)="create()">
        <div class="f-row">
          <div class="f"><label for="un">USERNAME</label><input id="un" formControlName="userName"></div>
          <div class="f" style="flex:2"><label for="fn">FULL NAME</label><input id="fn" formControlName="fullName"></div>
          <div class="f"><label for="pw">TEMPORARY PASSWORD (≥ 12 chars)</label>
            <input id="pw" type="password" formControlName="password"></div>
        </div>
        <div class="f-row">
          <div class="f"><label for="role">ROLE (system bundle — P02.2)</label>
            <select id="role" formControlName="role">
              @for (r of roles(); track r.role) { <option [value]="r.role">{{ r.role }}</option> }
            </select>
            <p class="hint">{{ selectedPermissions() }}</p>
          </div>
        </div>
        <button class="btn green" type="submit" [disabled]="form.invalid || busy()">
          {{ busy() ? 'Creating…' : 'Create user' }}
        </button>
      </form>
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class UsersComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  readonly users = signal<UserRow[]>([]);
  readonly roles = signal<RoleInfo[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    userName: ['', [Validators.required, Validators.minLength(3)]],
    fullName: ['', Validators.required],
    password: ['', [Validators.required, Validators.minLength(12)]],
    role: ['Technologist', Validators.required],
  });

  ngOnInit(): void {
    void this.load();
  }

  selectedPermissions(): string {
    const role = this.roles().find(r => r.role === this.form.getRawValue().role);
    return role ? `grants: ${role.permissions.join(' · ')}` : '';
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      const [users, roles] = await Promise.all([
        firstValueFrom(this.http.get<UserRow[]>(`${API_BASE_URL}/users`)),
        firstValueFrom(this.http.get<RoleInfo[]>(`${API_BASE_URL}/users/roles`)),
      ]);
      this.users.set(users);
      this.roles.set(roles);
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async setStatus(user: UserRow, action: 'lock' | 'unlock' | 'deactivate'): Promise<void> {
    if (action === 'deactivate' && !window.confirm(`Deactivate ${user.userName}? This blocks sign-in permanently.`)) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/users/${user.id}/set-status`, { action }));
      this.info.set(`${user.userName}: ${action} ✓`);
      await this.load();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  async resetPassword(user: UserRow): Promise<void> {
    const newPassword = window.prompt(`New temporary password for ${user.userName} (≥ 12 chars):`);
    if (!newPassword) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/users/${user.id}/reset-password`, { newPassword }));
      this.info.set(`Password for ${user.userName} reset ✓ — hand it over securely.`);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  async create(): Promise<void> {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      const value = this.form.getRawValue();
      await firstValueFrom(this.http.post(`${API_BASE_URL}/users`, {
        userName: value.userName, fullName: value.fullName,
        password: value.password, roles: [value.role],
      }));
      this.info.set(`User ${value.userName} created ✓ — hand over the temporary password securely.`);
      this.form.reset({ role: 'Technologist' });
      await this.load();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
