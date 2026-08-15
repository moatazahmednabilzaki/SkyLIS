import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from './config';

const TOKEN_KEY = 'skylis.client.token';
const TENANT_KEY = 'skylis.client.tenant';

/**
 * Dev authentication: exchanges a tenant id for a dev JWT from the Development-only
 * /dev/token endpoint. Replaced by the OpenIddict OIDC flow in later phases.
 * The frontend is never the security boundary — the token only unlocks UI affordances;
 * every rule is enforced server-side.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly tokenSignal = signal<string | null>(sessionStorage.getItem(TOKEN_KEY));
  readonly tenantId = signal<string | null>(sessionStorage.getItem(TENANT_KEY));
  readonly isAuthenticated = computed(() => this.tokenSignal() !== null);

  get token(): string | null {
    return this.tokenSignal();
  }

  async devLogin(tenantId: string): Promise<void> {
    const response = await firstValueFrom(this.http.post<{ token: string }>(
      `${API_BASE_URL}/dev/token`,
      { scope: 'tenant', tenantId, userName: 'dev-receptionist' }));
    sessionStorage.setItem(TOKEN_KEY, response.token);
    sessionStorage.setItem(TENANT_KEY, tenantId);
    this.tokenSignal.set(response.token);
    this.tenantId.set(tenantId);
  }

  logout(): void {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(TENANT_KEY);
    this.tokenSignal.set(null);
    this.tenantId.set(null);
  }
}
