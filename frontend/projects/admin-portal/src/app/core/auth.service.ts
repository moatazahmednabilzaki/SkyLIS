import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export const API_BASE_URL = 'http://localhost:5178/api/v1';
const TOKEN_KEY = 'skylis.admin.token';

/** Dev platform-operator authentication (Development-only /dev/token endpoint). */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly tokenSignal = signal<string | null>(sessionStorage.getItem(TOKEN_KEY));

  readonly isAuthenticated = computed(() => this.tokenSignal() !== null);

  get token(): string | null {
    return this.tokenSignal();
  }

  async devLogin(): Promise<void> {
    const response = await firstValueFrom(this.http.post<{ token: string }>(
      `${API_BASE_URL}/dev/token`, { scope: 'platform', userName: 'dev-platform-operator' }));
    sessionStorage.setItem(TOKEN_KEY, response.token);
    this.tokenSignal.set(response.token);
  }

  logout(): void {
    sessionStorage.removeItem(TOKEN_KEY);
    this.tokenSignal.set(null);
  }
}
