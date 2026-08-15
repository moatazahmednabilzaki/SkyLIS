import { Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
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
          Development sign-in: enter your tenant id to receive a dev token.
          The OIDC sign-in (MFA, SSO) replaces this screen in later phases.
        </p>
        @if (error()) { <div class="err">{{ error() }}</div> }
        <div class="f-row">
          <div class="f">
            <label for="tenant">TENANT ID (GUID)</label>
            <input id="tenant" class="mono" [formControl]="tenantId"
                   placeholder="00000000-0000-0000-0000-000000000000">
          </div>
        </div>
        <button class="btn" [disabled]="tenantId.invalid || busy()" (click)="signIn()">
          {{ busy() ? 'Signing in…' : 'Sign in (dev)' }}
        </button>
        <p class="hint" style="margin-top:10px">
          No tenant yet? Provision one from the Admin Portal (http://localhost:4201).
        </p>
      </div>
    </div>
  `,
  styles: `
    .wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; background: var(--navy); }
    .box { width: 420px; }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly tenantId = new FormControl('', {
    nonNullable: true,
    validators: [
      Validators.required,
      Validators.pattern(/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/),
    ],
  });
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  async signIn(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.devLogin(this.tenantId.value);
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
