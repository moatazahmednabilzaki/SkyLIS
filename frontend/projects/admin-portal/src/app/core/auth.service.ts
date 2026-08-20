import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export const API_BASE_URL = 'http://localhost:5178/api/v1';
const TOKEN_KEY = 'skylis.admin.token';
const REFRESH_KEY = 'skylis.admin.refresh';
const OPERATOR_KEY = 'skylis.admin.operator';

interface SessionResponse {
  token: string;
  refreshToken: string;
  userName: string;
  fullName: string;
}

/** Production platform-operator authentication with rotating refresh tokens. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly tokenSignal = signal<string | null>(sessionStorage.getItem(TOKEN_KEY));
  private refreshing: Promise<boolean> | null = null;

  readonly operatorName = signal<string | null>(sessionStorage.getItem(OPERATOR_KEY));
  readonly isAuthenticated = computed(() => this.tokenSignal() !== null);

  get token(): string | null {
    return this.tokenSignal();
  }

  async login(userName: string, password: string): Promise<void> {
    const response = await firstValueFrom(this.http.post<SessionResponse>(
      `${API_BASE_URL}/auth/platform-login`, { userName, password }));
    this.store(response);
  }

  /** One shared refresh attempt; concurrent 401s wait on the same rotation. */
  tryRefresh(): Promise<boolean> {
    this.refreshing ??= this.doRefresh().finally(() => (this.refreshing = null));
    return this.refreshing;
  }

  private async doRefresh(): Promise<boolean> {
    const refreshToken = sessionStorage.getItem(REFRESH_KEY);
    if (!refreshToken) return false;
    try {
      const response = await firstValueFrom(this.http.post<SessionResponse>(
        `${API_BASE_URL}/auth/refresh`, { refreshToken }));
      this.store(response);
      return true;
    } catch {
      return false;
    }
  }

  logout(): void {
    const refreshToken = sessionStorage.getItem(REFRESH_KEY);
    if (refreshToken) {
      // Fire-and-forget revocation; the local session dies either way.
      this.http.post(`${API_BASE_URL}/auth/logout`, { refreshToken }).subscribe({ error: () => {} });
    }
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(REFRESH_KEY);
    sessionStorage.removeItem(OPERATOR_KEY);
    this.tokenSignal.set(null);
    this.operatorName.set(null);
  }

  private store(response: SessionResponse): void {
    sessionStorage.setItem(TOKEN_KEY, response.token);
    sessionStorage.setItem(REFRESH_KEY, response.refreshToken);
    sessionStorage.setItem(OPERATOR_KEY, response.fullName);
    this.tokenSignal.set(response.token);
    this.operatorName.set(response.fullName);
  }
}
