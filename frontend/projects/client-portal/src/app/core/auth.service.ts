import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from './config';

const TOKEN_KEY = 'skylis.client.token';
const TENANT_KEY = 'skylis.client.tenant';

export interface SessionUser {
  userName: string;
  fullName: string;
  roles: string[];
}

const USER_KEY = 'skylis.client.user';

/**
 * Real credential authentication (M02): username + password against /auth/login.
 * The frontend is never the security boundary — the token only unlocks UI affordances;
 * every rule is enforced server-side. OIDC (MFA, SSO) replaces this flow later.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly tokenSignal = signal<string | null>(sessionStorage.getItem(TOKEN_KEY));
  readonly tenantId = signal<string | null>(sessionStorage.getItem(TENANT_KEY));
  readonly user = signal<SessionUser | null>(
    JSON.parse(sessionStorage.getItem(USER_KEY) ?? 'null'));
  readonly isAuthenticated = computed(() => this.tokenSignal() !== null);

  get token(): string | null {
    return this.tokenSignal();
  }

  async login(tenantId: string, userName: string, password: string): Promise<void> {
    const response = await firstValueFrom(this.http.post<{
      token: string; userName: string; fullName: string; roles: string[];
    }>(`${API_BASE_URL}/auth/login`, { tenantId, userName, password }));
    const sessionUser: SessionUser = {
      userName: response.userName, fullName: response.fullName, roles: response.roles,
    };
    sessionStorage.setItem(TOKEN_KEY, response.token);
    sessionStorage.setItem(TENANT_KEY, tenantId);
    sessionStorage.setItem(USER_KEY, JSON.stringify(sessionUser));
    this.tokenSignal.set(response.token);
    this.tenantId.set(tenantId);
    this.user.set(sessionUser);
  }

  logout(): void {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(TENANT_KEY);
    sessionStorage.removeItem(USER_KEY);
    this.tokenSignal.set(null);
    this.tenantId.set(null);
    this.user.set(null);
  }
}
