import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { Role } from './auth.models';

/** Where each role lands after signing in. A user with several roles gets the most powerful one's home. */
export function homeFor(roles: readonly Role[]): string {
  if (roles.includes('Admin')) return '/admin';
  if (roles.includes('Attendant')) return '/gate';
  return '/my';
}

/**
 * Route guards are a USABILITY feature, not a security boundary: anything shipped to the browser can be bypassed.
 * The real enforcement is the API, which checks the JWT's roles on every request and returns 401/403.
 */
export const authGuard: CanActivateFn = (_route, state): boolean | UrlTree => {
  const auth = inject(AuthService);
  return (
    auth.isAuthenticated() ||
    inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })
  );
};

/** For login/register: someone already signed in has no business there. */
export const guestGuard: CanActivateFn = (): boolean | UrlTree => {
  const auth = inject(AuthService);
  return !auth.isAuthenticated() || inject(Router).createUrlTree([homeFor(auth.user()!.roles)]);
};

export function roleGuard(...roles: Role[]): CanActivateFn {
  return (_route, state): boolean | UrlTree => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isAuthenticated())
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    return auth.hasAnyRole(roles) || router.createUrlTree(['/forbidden']);
  };
}
