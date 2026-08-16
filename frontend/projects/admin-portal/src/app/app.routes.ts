import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { AuthService } from './core/auth.service';

const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isAuthenticated() ? true : router.createUrlTree(['/login']);
};

export const appRoutes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'tenants' },
      {
        path: 'tenants',
        loadComponent: () => import('./features/tenants/tenants.component').then(m => m.TenantsComponent),
      },
      {
        path: 'health',
        loadComponent: () => import('./features/health/health.component').then(m => m.HealthComponent),
      },
      {
        path: 'country-packs',
        loadComponent: () =>
          import('./features/country-packs/country-packs.component').then(m => m.CountryPacksComponent),
      },
      {
        path: 'master-data',
        loadComponent: () =>
          import('./features/master-data/master-data.component').then(m => m.MasterDataComponent),
      },
      {
        path: 'plans',
        loadComponent: () => import('./features/plans/plans.component').then(m => m.PlansComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
