import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import {
  PaymentResult, RegisterVisitRequest, RegisteredVisit, VisitDetails,
} from '../../core/api.types';

/** Centralized API access for visits, sample actions, and payments. */
@Injectable({ providedIn: 'root' })
export class VisitsApi {
  private readonly http = inject(HttpClient);

  register(request: RegisterVisitRequest): Observable<RegisteredVisit> {
    return this.http.post<RegisteredVisit>(`${API_BASE_URL}/visits`, request);
  }

  get(visitId: string): Observable<VisitDetails> {
    return this.http.get<VisitDetails>(`${API_BASE_URL}/visits/${visitId}`);
  }

  collectSample(visitId: string, sampleId: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}/visits/${visitId}/samples/${sampleId}/collect`, {});
  }

  receiveSample(visitId: string, sampleId: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}/visits/${visitId}/samples/${sampleId}/receive`, {});
  }

  rejectSample(visitId: string, sampleId: string, reasonCode: string): Observable<{ recollectionSampleId: string }> {
    return this.http.post<{ recollectionSampleId: string }>(
      `${API_BASE_URL}/visits/${visitId}/samples/${sampleId}/reject`, { reasonCode });
  }

  capturePayment(invoiceId: string, amount: number, currency: string, method: string): Observable<PaymentResult> {
    return this.http.post<PaymentResult>(
      `${API_BASE_URL}/billing/invoices/${invoiceId}/payments`, { amount, currency, method });
  }
}
