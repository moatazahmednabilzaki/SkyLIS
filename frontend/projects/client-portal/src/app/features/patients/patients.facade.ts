import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PatientsApi } from './patients.api';
import { PatientSearchResult, RegisterPatientRequest, problemMessage } from '../../core/api.types';

/** Signal-based feature store orchestrating patient search & registration state. */
@Injectable({ providedIn: 'root' })
export class PatientsFacade {
  private readonly api = inject(PatientsApi);

  readonly results = signal<PatientSearchResult[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly searched = signal(false);

  async search(term: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.results.set(await firstValueFrom(this.api.search(term)));
      this.searched.set(true);
    } catch (e) {
      this.error.set(problemMessage(e));
      this.results.set([]);
    } finally {
      this.loading.set(false);
    }
  }

  /** Registers the patient and returns the new id (throws with a display message on failure). */
  async register(request: RegisterPatientRequest): Promise<string> {
    this.error.set(null);
    try {
      const { id } = await firstValueFrom(this.api.register(request));
      return id;
    } catch (e) {
      const message = problemMessage(e);
      this.error.set(message);
      throw new Error(message);
    }
  }
}
