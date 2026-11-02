import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { API_BASE_URL } from '../api/api.config';
import { AuthService } from './auth.service';

/**
 * 1. Adds "Authorization: Bearer <token>" to calls to OUR API (never to third-party URLs: that would leak the token).
 * 2. On a 401 it refreshes the session once and replays the request, so an expired 15-minute token is invisible to the user.
 * 3. If the refresh itself fails, the session is really over: clear it and send the user to the login page.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const apiBase = inject(API_BASE_URL);

  if (!req.url.startsWith(apiBase) || isAuthEndpoint(req.url, apiBase)) return next(req);

  const withToken = (r: HttpRequest<unknown>) => {
    const token = auth.token();
    return token ? r.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : r;
  };

  return next(withToken(req)).pipe(
    catchError((error: unknown) => {
      if (
        !(error instanceof HttpErrorResponse) ||
        error.status !== 401 ||
        !auth.isAuthenticated()
      ) {
        return throwError(() => error);
      }

      return auth.refresh().pipe(
        // Only a failed REFRESH ends the session. A failure of the replayed request (403, 500...) is just that request's error.
        catchError((refreshError: unknown) => {
          auth.clearSession();
          void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
          return throwError(() => refreshError);
        }),
        switchMap(() => next(withToken(req))), // replay exactly once, with the new token
      );
    }),
  );
};

/** Login/register/refresh/logout must never be refreshed-and-retried: a 401 there means "wrong credentials", not "expired token". */
function isAuthEndpoint(url: string, apiBase: string): boolean {
  const authBase = `${apiBase}/auth/`;
  return (
    url.startsWith(authBase) &&
    /^(login|register|refresh|logout)(\?|$)/.test(url.slice(authBase.length))
  );
}
