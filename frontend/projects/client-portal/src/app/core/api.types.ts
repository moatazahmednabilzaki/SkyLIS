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

export interface DuplicateCandidate {
  id: string;
  patientNumber: string;
  fullName: string;
  mobile: string;
  dateOfBirth: string; // yyyy-MM-dd
  lastVisitAtUtc: string | null;
  visitCount: number;
}

export interface DuplicateGroup {
  matchedOn: string;
  patients: DuplicateCandidate[];
}

export interface MergePatientsRequest {
  survivorId: string;
  duplicateId: string;
  reason: string;
}

export interface RegisterVisitRequest {
  patientId: string;
  branchId: string;
  testIds: string[];
  panelIds?: string[];
  isStat: boolean;
  statReason: string | null;
}

export interface CatalogPanel {
  id: string;
  code: string;
  name: string;
  price: number;
  currency: string;
  isActive: boolean;
  members: { testId: string; testCode: string; testName: string }[];
}

export interface Department {
  id: string;
  code: string;
  name: string;
}

export interface Branch {
  id: string;
  code: string;
  name: string;
  address: string | null;
  phone: string | null;
  isMain: boolean;
  isActive: boolean;
  departments: Department[];
}

export interface CatalogCondition {
  id: string;
  name: string;
  delayMinutes: number | null;
  compatibilityGroup: string;
}

export interface CatalogSampleType {
  id: string;
  name: string;
  containerName: string;
  conditions: CatalogCondition[];
}

export interface CatalogTest {
  id: string;
  code: string;
  name: string;
  department: string;
  status: string;
  origin: string;
  price: number | null;
  currency: string | null;
  sampleTypeId: string;
  requiredConditionId: string | null;
  hasResultSchema: boolean;
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

export interface VisitResult {
  resultId: string;
  visitTestId: string;
  testCode: string;
  value: number;
  unit: string;
  flag: string;
  status: string;
  isAmended: boolean;
  valueBeforeAmendment: number | null;
  amendmentReason: string | null;
}

export interface InvoicePayment {
  id: string;
  amount: number;
  currency: string;
  method: string;
  isRefund: boolean;
  reason: string | null;
  capturedAtUtc: string;
}

export interface CreditNoteLine {
  id: string;
  creditNoteNumber: string;
  amount: number;
  currency: string;
  reason: string;
  issuedAtUtc: string;
}

export interface InvoiceDetails {
  id: string;
  invoiceNumber: string;
  visitId: string;
  visitNumber: string;
  branchCode: string;
  status: string;
  total: number;
  discountAmount: number;
  discountReason: string | null;
  creditedAmount: number;
  paid: number;
  refunded: number;
  balance: number;
  currency: string;
  payments: InvoicePayment[];
  creditNotes: CreditNoteLine[];
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
