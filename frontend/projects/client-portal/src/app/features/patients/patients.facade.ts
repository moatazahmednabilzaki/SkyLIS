import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PatientsApi } from './patients.api';
import {
  DuplicateCandidate, DuplicateGroup, PatientSearchResult, RegisterPatientRequest, problemMessage,
} from '../../core/api.types';

/** Signal-based feature store orchestrating patient search & registration state. */
@Injectable({ providedIn: 'root' })
export class PatientsFacade {
  private readonly api = inject(PatientsApi);

  readonly results = signal<PatientSearchResult[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly searched = signal(false);

  readonly duplicates = signal<DuplicateGroup[]>([]);
  readonly scanned = signal(false);

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

  /** P04.4: refreshes the duplicate-candidate groups shown in the merge console. */
  async scanDuplicates(): Promise<void> {
    this.error.set(null);
    try {
      this.duplicates.set(await firstValueFrom(this.api.duplicates()));
      this.scanned.set(true);
    } catch (e) {
      this.error.set(problemMessage(e));
      this.duplicates.set([]);
    }
  }

  /** Merges every other member of the group into the survivor, then rescans. */
  async mergeGroupInto(group: DuplicateGroup, survivor: DuplicateCandidate, reason: string): Promise<void> {
    this.error.set(null);
    try {
      for (const duplicate of group.patients.filter(p => p.id !== survivor.id)) {
        await firstValueFrom(this.api.merge({
          survivorId: survivor.id, duplicateId: duplicate.id, reason,
        }));
      }
    } catch (e) {
      this.error.set(problemMessage(e));
    }
    await this.scanDuplicates();
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
