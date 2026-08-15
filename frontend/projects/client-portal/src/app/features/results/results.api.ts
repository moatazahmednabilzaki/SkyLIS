import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/config';

export interface PendingEntry {
  visitId: string;
  visitNumber: string;
  visitTestId: string;
  testCode: string;
  patientName: string;
  sampleBarcode: string;
  isStat: boolean;
  unit: string | null;
  refLow: number | null;
  refHigh: number | null;
}

export interface EnteredResult {
  resultId: string;
  testCode: string;
  value: number;
  unit: string;
  flag: string;
  deltaFlagged: boolean;
  previousValue: number | null;
  status: string;
  autoVerified: boolean;
  criticalFlagged: boolean;
}

export interface ResultQueueItem {
  resultId: string;
  visitId: string;
  visitNumber: string;
  patientName: string;
  testCode: string;
  value: number;
  unit: string;
  flag: string;
  deltaFlagged: boolean;
  previousValue: number | null;
  status: string;
  enteredAtUtc: string;
}

export interface CriticalQueueItem {
  resultId: string;
  visitNumber: string;
  patientName: string;
  testCode: string;
  value: number;
  unit: string;
  flag: string;
  criticalState: string;
  flaggedAtUtc: string;
  calledPerson: string | null;
  readBackConfirmed: boolean;
}

/** Centralized API access for M09 (EAA: no HTTP in components). */
@Injectable({ providedIn: 'root' })
export class ResultsApi {
  private readonly http = inject(HttpClient);

  pendingEntry(): Observable<PendingEntry[]> {
    return this.http.get<PendingEntry[]>(`${API_BASE_URL}/results/pending-entry`);
  }

  enter(visitId: string, visitTestId: string, value: number): Observable<EnteredResult> {
    return this.http.post<EnteredResult>(`${API_BASE_URL}/visits/${visitId}/results`, { visitTestId, value });
  }

  technicalQueue(): Observable<ResultQueueItem[]> {
    return this.http.get<ResultQueueItem[]>(`${API_BASE_URL}/results/technical-queue`);
  }

  medicalQueue(): Observable<ResultQueueItem[]> {
    return this.http.get<ResultQueueItem[]>(`${API_BASE_URL}/results/medical-queue`);
  }

  criticalQueue(): Observable<CriticalQueueItem[]> {
    return this.http.get<CriticalQueueItem[]>(`${API_BASE_URL}/results/critical-queue`);
  }

  acceptTechnical(resultId: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}/results/${resultId}/accept-technical`, {});
  }

  rerun(resultId: string, reason: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}/results/${resultId}/rerun`, { reason });
  }

  validateMedical(resultId: string, interpretiveComment: string | null, signatureIntent: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}/results/${resultId}/validate-medical`,
      { interpretiveComment, signatureIntent });
  }

  documentCriticalCall(resultId: string, calledPerson: string, phone: string, readBackConfirmed: boolean): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}/results/${resultId}/critical/document-call`,
      { calledPerson, phone, readBackConfirmed });
  }
}
