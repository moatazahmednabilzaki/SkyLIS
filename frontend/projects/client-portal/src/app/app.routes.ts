import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const appRoutes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent),
      },
      {
        path: 'analytics',
        loadComponent: () => import('./features/dashboard/analytics.component').then(m => m.AnalyticsComponent),
      },
      {
        path: 'patients',
        loadComponent: () => import('./features/patients/patients.component').then(m => m.PatientsComponent),
      },
      {
        path: 'patients/:id',
        loadComponent: () => import('./features/patients/patient-360.component').then(m => m.Patient360Component),
      },
      {
        path: 'visits/new',
        loadComponent: () => import('./features/visits/visit-register.component').then(m => m.VisitRegisterComponent),
      },
      {
        path: 'visits/:id',
        loadComponent: () => import('./features/visits/visit-details.component').then(m => m.VisitDetailsComponent),
      },
      {
        path: 'reception',
        loadComponent: () => import('./features/worklists/reception.component').then(m => m.ReceptionComponent),
      },
      {
        path: 'phlebotomist',
        loadComponent: () => import('./features/worklists/phlebotomist.component').then(m => m.PhlebotomistComponent),
      },
      {
        path: 'results',
        loadComponent: () => import('./features/results/results-entry.component').then(m => m.ResultsEntryComponent),
      },
      {
        path: 'validation',
        loadComponent: () => import('./features/results/validation.component').then(m => m.ValidationComponent),
      },
      {
        path: 'critical',
        loadComponent: () => import('./features/results/critical.component').then(m => m.CriticalComponent),
      },
      {
        path: 'reports',
        loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent),
      },
      {
        path: 'audit',
        loadComponent: () => import('./features/audit/audit.component').then(m => m.AuditComponent),
      },
      {
        path: 'users',
        loadComponent: () => import('./features/users/users.component').then(m => m.UsersComponent),
      },
      {
        path: 'branches',
        loadComponent: () => import('./features/org/branches.component').then(m => m.BranchesComponent),
      },
      {
        path: 'cashier',
        loadComponent: () => import('./features/billing/cashier.component').then(m => m.CashierComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
