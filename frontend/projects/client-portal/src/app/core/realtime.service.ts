import { Injectable, OnDestroy, inject } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject, Observable, filter, map } from 'rxjs';
import { AuthService } from './auth.service';
import { API_BASE_URL } from './config';

/**
 * FR-SYS-010 live worklists. The hub pushes HINTS ({area}) — components react by
 * reloading from the API (the system of record); pushed payloads are never trusted
 * as data. The connection authenticates with the bearer token; the server assigns
 * the tenant group from the token claims.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly changes = new Subject<string>();
  private connection: HubConnection | null = null;

  /** Emits when the given worklist area changed server-side. */
  onArea(area: string): Observable<void> {
    void this.ensureConnected();
    return this.changes.pipe(filter(a => a === area), map(() => undefined));
  }

  private async ensureConnected(): Promise<void> {
    if (this.connection || !this.auth.isAuthenticated()) return;

    this.connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE_URL.replace('/api/v1', '')}/hubs/worklists`, {
        accessTokenFactory: () => this.auth.token ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('worklistChanged', (hint: { area: string }) =>
      this.changes.next(hint.area));

    try {
      await this.connection.start();
    } catch {
      // Reconnect handling: retry on next subscription; the UI stays functional
      // without live updates (hints are an enhancement, not a dependency).
      this.connection = null;
    }
  }

  ngOnDestroy(): void {
    void this.connection?.stop();
  }
}
