import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  template: `
    <div class="wrap">
      <div class="card box">
        <h3>Sky LIS — Admin Portal</h3>
        <p class="hint" style="margin-bottom:12px">
          National Technology platform console. Development sign-in issues a
          platform-operator dev token; OIDC + MFA replaces this later.
        </p>
        @if (error()) { <div class="err">{{ error() }}</div> }
        <button class="btn" [disabled]="busy()" (click)="signIn()">
          {{ busy() ? 'Signing in…' : 'Sign in as platform operator (dev)' }}
        </button>
      </div>
    </div>
  `,
  styles: `
    .wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; }
    .box { width: 420px; }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  async signIn(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.devLogin();
      await this.router.navigateByUrl('/tenants');
    } catch {
      this.error.set('Could not reach the API. Is it running on http://localhost:5178 in Development mode?');
    } finally {
      this.busy.set(false);
    }
  }
}
