// DTO contracts mirroring the SkyLIS.Api response/request records.

export interface PatientSearchResult {
  id: string;
  patientNumber: string;
  fullName: string;
  mobileMasked: string;
  gender: string;
  age: number;
  lastVisitAtUtc: string | null;
}

export interface RegisterPatientRequest {
  fullName: string;
  sex: 'Female' | 'Male';
  dateOfBirth: string; // yyyy-MM-dd
  mobile: string;
  nationalId: string | null;
}

export interface RegisterVisitRequest {
  patientId: string;
  testIds: string[];
  isStat: boolean;
  statReason: string | null;
}

export interface RegisteredSample {
  sampleId: string;
  barcode: string;
  state: string;
  condition: string | null;
  readyAtUtc: string | null;
}

export interface RegisteredVisit {
  visitId: string;
  visitNumber: string;
  invoiceId: string;
  invoiceNumber: string;
  total: number;
  currency: string;
  samples: RegisteredSample[];
}

export interface VisitTestLine {
  id: string;
  testCode: string;
  status: string;
  price: number;
  currency: string;
  sampleId: string;
}

export interface VisitSample {
  id: string;
  barcode: string;
  state: string;
  condition: string | null;
  readyAtUtc: string | null;
  rejectionReasonCode: string | null;
}

export interface VisitDetails {
  id: string;
  visitNumber: string;
  status: string;
  isStat: boolean;
  patientId: string;
  patientName: string;
  registeredAtUtc: string;
  tests: VisitTestLine[];
  samples: VisitSample[];
}

export interface PaymentResult {
  invoiceId: string;
  status: string;
  paid: number;
  balance: number;
  currency: string;
}

export interface ProblemDetails {
  status?: number;
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** Flattens an API ProblemDetails error into one displayable message. */
export function problemMessage(error: unknown): string {
  const problem = (error as { error?: ProblemDetails })?.error;
  if (problem?.errors) {
    return Object.values(problem.errors).flat().join(' ');
  }
  return problem?.detail ?? problem?.title ?? 'The request failed. Please try again.';
}
