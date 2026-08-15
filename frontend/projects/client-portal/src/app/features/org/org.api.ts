import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { Branch, CatalogSampleType, CatalogTest } from '../../core/api.types';

/** API access for branches & departments (P03.2). */
@Injectable({ providedIn: 'root' })
export class OrgApi {
  private readonly http = inject(HttpClient);

  listBranches(): Observable<Branch[]> {
    return this.http.get<Branch[]>(`${API_BASE_URL}/org/branches`);
  }

  createBranch(code: string, name: string, address: string | null, phone: string | null): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${API_BASE_URL}/org/branches`, { code, name, address, phone });
  }

  addDepartment(branchId: string, code: string, name: string): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${API_BASE_URL}/org/branches/${branchId}/departments`, { code, name });
  }

  setBranchActive(branchId: string, isActive: boolean): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}/org/branches/${branchId}/set-active`, { isActive });
  }
}

/** Read access for the tenant catalog (test picker, sample taxonomy). */
@Injectable({ providedIn: 'root' })
export class CatalogApi {
  private readonly http = inject(HttpClient);

  listTests(status?: string): Observable<CatalogTest[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.http.get<CatalogTest[]>(`${API_BASE_URL}/catalog/tests`, { params });
  }

  listSampleTypes(): Observable<CatalogSampleType[]> {
    return this.http.get<CatalogSampleType[]>(`${API_BASE_URL}/catalog/sample-types`);
  }
}
