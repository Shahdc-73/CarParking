import { Injector, runInInjectionContext, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { admin, attendant, driver } from '../testing';
import { User } from './auth.models';
import { authGuard, guestGuard, homeFor, roleGuard } from './auth.guards';
import { AuthService } from './auth.service';

describe('route guards', () => {
  const current = signal<User | null>(null);
  let router: Router;
  let injector: Injector;

  beforeEach(() => {
    current.set(null);
    const stub = {
      user: current.asReadonly(),
      isAuthenticated: () => current() !== null,
      hasAnyRole: (roles: string[]) =>
        roles.some((r) => (current()?.roles as string[] | undefined)?.includes(r)),
    };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: stub }],
    });
    router = TestBed.inject(Router);
    injector = TestBed.inject(Injector);
  });

  const run = (guard: CanActivateFn, url = '/gate') =>
    runInInjectionContext(injector, () =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    ) as boolean | UrlTree;

  const path = (result: boolean | UrlTree) =>
    result instanceof UrlTree ? router.serializeUrl(result) : result;

  describe('homeFor', () => {
    it('sends each role to its own area, most powerful role first', () => {
      expect(homeFor(['Driver'])).toBe('/my');
      expect(homeFor(['Attendant'])).toBe('/gate');
      expect(homeFor(['Admin'])).toBe('/admin');
      expect(homeFor(['Driver', 'Admin'])).toBe('/admin');
      expect(homeFor(['Driver', 'Attendant'])).toBe('/gate');
      expect(homeFor([])).toBe('/my');
    });
  });

  describe('authGuard', () => {
    it('lets signed-in users through', () => {
      current.set(driver);
      expect(run(authGuard)).toBe(true);
    });

    it('sends anonymous users to login, remembering where they were going', () => {
      expect(path(run(authGuard, '/my?tab=history'))).toBe(
        '/login?returnUrl=%2Fmy%3Ftab%3Dhistory',
      );
    });
  });

  describe('guestGuard', () => {
    it('lets anonymous users see login/register', () => {
      expect(run(guestGuard)).toBe(true);
    });

    it('sends signed-in users to their own home instead', () => {
      current.set(attendant);
      expect(path(run(guestGuard))).toBe('/gate');
    });
  });

  describe('roleGuard', () => {
    it('allows a matching role', () => {
      current.set(attendant);
      expect(run(roleGuard('Admin', 'Attendant'))).toBe(true);
    });

    it('sends a signed-in user with the wrong role to the forbidden page', () => {
      current.set(driver);
      expect(path(run(roleGuard('Admin')))).toBe('/forbidden');
    });

    it('sends anonymous users to login', () => {
      expect(path(run(roleGuard('Admin'), '/admin'))).toBe('/login?returnUrl=%2Fadmin');
    });

    it('an admin passes admin-only routes', () => {
      current.set(admin);
      expect(run(roleGuard('Admin'))).toBe(true);
    });
  });
});
