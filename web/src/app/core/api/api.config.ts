import { InjectionToken } from '@angular/core';

/**
 * Base path of the backend API. Relative on purpose: in development the Angular dev server proxies /api to the
 * .NET API, and in Docker nginx does the same. The browser therefore only ever talks to ONE origin, which keeps
 * the refresh-token cookie same-site (SameSite=Strict) and removes the need for CORS.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '/api/v1',
});
