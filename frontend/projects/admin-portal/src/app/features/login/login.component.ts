import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  template: `
    <div class="wrap">
      <div class="card box">
        <h3>Sky LIS — Admin Portal</h3>
        <p class="hint" style="margin-bottom:12px">
          National Technology platform console. Operator accounts only — tenant staff
          sign in on their own portal.
        </p>
        @if (error()) { <div class="err">{{ error() }}</div> }
        <form (ngSubmit)="signIn()">
          <div class="f" style="margin-bottom:10px">
            <label for="userName">OPERATOR USERNAME</label>
            <input id="userName" [formControl]="userName" autocomplete="username">
          </div>
          <div class="f" style="margin-bottom:14px">
            <label for="password">PASSWORD</label>
            <input id="password" type="password" [formControl]="password" autocomplete="current-password">
          </div>
          <button class="btn" type="submit" [disabled]="userName.invalid || password.invalid || busy()">
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>
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
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly userName = this.fb.nonNullable.control('', Validators.required);
  readonly password = this.fb.nonNullable.control('', Validators.required);

  async signIn(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.userName.value, this.password.value);
      await this.router.navigateByUrl('/tenants');
    } catch {
      this.error.set('Sign-in failed. Check the credentials — five consecutive misses lock the account.');
    } finally {
      this.busy.set(false);
    }
  }
}
