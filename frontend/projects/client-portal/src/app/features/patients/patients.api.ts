import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import {
  DuplicateGroup, MergePatientsRequest, PatientSearchResult, RegisterPatientRequest,
} from '../../core/api.types';

/** Centralized API access for the patients feature (EAA: no HTTP in components). */
@Injectable({ providedIn: 'root' })
export class PatientsApi {
  private readonly http = inject(HttpClient);

  search(term: string): Observable<PatientSearchResult[]> {
    return this.http.get<PatientSearchResult[]>(
      `${API_BASE_URL}/patients/search`, { params: { term } });
  }

  register(request: RegisterPatientRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${API_BASE_URL}/patients`, request);
  }

  duplicates(): Observable<DuplicateGroup[]> {
    return this.http.get<DuplicateGroup[]>(`${API_BASE_URL}/patients/duplicates`);
  }

  merge(request: MergePatientsRequest): Observable<unknown> {
    return this.http.post(`${API_BASE_URL}/patients/merge`, request);
  }
}
