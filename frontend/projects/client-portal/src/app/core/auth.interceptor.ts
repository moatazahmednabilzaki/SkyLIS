import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Attaches the bearer token; on 401 rotates the refresh token once and retries. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isAuthCall = request.url.includes('/auth/');
  const withToken = (req: HttpRequest<unknown>) => auth.token && !isAuthCall
    ? req.clone({ setHeaders: { Authorization: `Bearer ${auth.token}` } })
    : req;

  return next(withToken(request)).pipe(
    catchError(error => {
      if (error?.status !== 401 || isAuthCall) {
        return throwError(() => error);
      }
      return from(auth.tryRefresh()).pipe(
        switchMap(renewed => {
          if (renewed) return next(withToken(request));
          auth.logout();
          void router.navigateByUrl('/login');
          return throwError(() => error);
        }),
      );
    }),
  );
};
