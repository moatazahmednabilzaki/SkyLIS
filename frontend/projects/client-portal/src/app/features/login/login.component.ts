import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { problemMessage } from '../../core/api.types';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  template: `
    <div class="wrap">
      <div class="card box">
        <h3>Sky LIS — Client Portal</h3>
        <p class="hint" style="margin-bottom:12px">
          Sign in with your personal account (M02). Credential sharing breaches the
          service agreement — the audit trail depends on personal logins.
        </p>
        @if (error()) { <div class="err">{{ error() }}</div> }
        <form [formGroup]="form" (ngSubmit)="signIn()">
          <div class="f-row">
            <div class="f">
              <label for="tenant">TENANT ID (dev — subdomain resolves this in production)</label>
              <input id="tenant" class="mono" formControlName="tenantId"
                     placeholder="00000000-0000-0000-0000-000000000000">
            </div>
          </div>
          <div class="f-row">
            <div class="f">
              <label for="username">USERNAME</label>
              <input id="username" formControlName="userName" autocomplete="username">
            </div>
            <div class="f">
              <label for="password">PASSWORD</label>
              <input id="password" type="password" formControlName="password" autocomplete="current-password">
            </div>
          </div>
          <button class="btn" type="submit" [disabled]="form.invalid || busy()">
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>
        <p class="hint" style="margin-top:10px">
          No tenant yet? Provision one from the Admin Portal (http://localhost:4201) —
          it creates the initial Tenant Admin account.
        </p>
      </div>
    </div>
  `,
  styles: `
    .wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; background: var(--navy); }
    .box { width: 460px; }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    tenantId: ['', [Validators.required,
      Validators.pattern(/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/)]],
    userName: ['', [Validators.required, Validators.minLength(3)]],
    password: ['', Validators.required],
  });
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  async signIn(): Promise<void> {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const { tenantId, userName, password } = this.form.getRawValue();
      await this.auth.login(tenantId, userName, password);
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
