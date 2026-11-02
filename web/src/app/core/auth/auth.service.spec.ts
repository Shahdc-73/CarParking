import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authResponse, driver } from '../testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const signIn = () => {
    auth.login({ email: driver.email, password: 'x' }).subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse('token-1'));
  };

  it('starts signed out', () => {
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.user()).toBeNull();
    expect(auth.token()).toBeNull();
  });

  it('login keeps the user and the access token in memory and sends the cookie', () => {
    let result: unknown;
    auth.login({ email: driver.email, password: 'x' }).subscribe((u) => (result = u));

    const req = http.expectOne('/api/v1/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true); // lets the browser store the refresh cookie
    req.flush(authResponse('token-1'));

    expect(result).toEqual(driver);
    expect(auth.user()).toEqual(driver);
    expect(auth.token()).toBe('token-1');
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('never persists the access token in web storage', () => {
    signIn();
    expect(JSON.stringify({ ...localStorage })).not.toContain('token-1');
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('token-1');
  });

  it('a failed login leaves the user signed out and surfaces the error', () => {
    let failed = false;
    auth.login({ email: 'a@b.c', password: 'bad' }).subscribe({ error: () => (failed = true) });
    http
      .expectOne('/api/v1/auth/login')
      .flush({ title: 'Invalid email or password.' }, { status: 401, statusText: 'Unauthorized' });

    expect(failed).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('register signs the new user in', () => {
    auth.register({ email: 'new@x.y', password: 'Passw0rd1', fullName: 'New' }).subscribe();
    http.expectOne('/api/v1/auth/register').flush(authResponse('t', driver));
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('logout clears the session once the server confirms', () => {
    signIn();
    auth.logout().subscribe();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.token()).toBeNull();
  });

  it('logout still signs the user out locally when the server cannot be reached', () => {
    signIn();
    let finished = false;
    auth.logout().subscribe(() => (finished = true));
    http.expectOne('/api/v1/auth/logout').error(new ProgressEvent('error'));
    expect(finished).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('concurrent refresh calls share ONE request (reusing a rotated refresh token would be treated as theft)', () => {
    const tokens: string[] = [];
    auth.refresh().subscribe((r) => tokens.push(r.accessToken));
    auth.refresh().subscribe((r) => tokens.push(r.accessToken));
    auth.refresh().subscribe((r) => tokens.push(r.accessToken));

    http.expectOne('/api/v1/auth/refresh').flush(authResponse('token-2')); // expectOne fails if there were 2+

    expect(tokens).toEqual(['token-2', 'token-2', 'token-2']);
    expect(auth.token()).toBe('token-2');
  });

  it('after a refresh finishes, the next refresh makes a new request', () => {
    auth.refresh().subscribe();
    http.expectOne('/api/v1/auth/refresh').flush(authResponse('token-2'));
    auth.refresh().subscribe();
    http.expectOne('/api/v1/auth/refresh').flush(authResponse('token-3'));
    expect(auth.token()).toBe('token-3');
  });

  it('a failed refresh can be retried later (the shared request is not stuck)', () => {
    auth.refresh().subscribe({ error: () => undefined });
    http.expectOne('/api/v1/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    auth.refresh().subscribe();
    http.expectOne('/api/v1/auth/refresh').flush(authResponse('token-9'));
    expect(auth.token()).toBe('token-9');
  });

  it('restoreSession signs the user in again after a page reload when the cookie is still valid', async () => {
    const restored = auth.restoreSession();
    http.expectOne('/api/v1/auth/refresh').flush(authResponse('token-5'));
    await restored;
    expect(auth.user()).toEqual(driver);
  });

  it('restoreSession quietly stays signed out when there is no valid cookie', async () => {
    const restored = auth.restoreSession();
    http.expectOne('/api/v1/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(restored).resolves.toBeUndefined();
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('hasAnyRole matches on any of the given roles', () => {
    signIn();
    expect(auth.hasAnyRole(['Driver'])).toBe(true);
    expect(auth.hasAnyRole(['Admin', 'Driver'])).toBe(true);
    expect(auth.hasAnyRole(['Admin', 'Attendant'])).toBe(false);
    expect(auth.hasAnyRole([])).toBe(false);
  });
});
