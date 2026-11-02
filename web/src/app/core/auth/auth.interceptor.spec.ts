import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { authResponse, driver } from '../testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  const signIn = () => {
    auth.login({ email: driver.email, password: 'x' }).subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse('old-token'));
  };

  const unauthorized = { status: 401, statusText: 'Unauthorized' };

  it('adds the bearer token to API calls', () => {
    signIn();
    client.get('/api/v1/lots').subscribe();
    expect(http.expectOne('/api/v1/lots').request.headers.get('Authorization')).toBe(
      'Bearer old-token',
    );
  });

  it('sends no Authorization header when signed out', () => {
    client.get('/api/v1/lots').subscribe();
    expect(http.expectOne('/api/v1/lots').request.headers.has('Authorization')).toBe(false);
  });

  it('never leaks the token to third-party URLs', () => {
    signIn();
    client.get('https://fonts.example.com/x.css').subscribe();
    expect(
      http.expectOne('https://fonts.example.com/x.css').request.headers.has('Authorization'),
    ).toBe(false);
  });

  it('a wrong password (401 from /auth/login) is not "refreshed and retried"', () => {
    signIn();
    let status = 0;
    client.post('/api/v1/auth/login', {}).subscribe({ error: (e) => (status = e.status) });
    http.expectOne('/api/v1/auth/login').flush(null, unauthorized);
    expect(status).toBe(401);
    http.expectNone('/api/v1/auth/refresh');
  });

  it('on a 401 it refreshes ONCE and replays the request with the new token, invisibly to the caller', () => {
    signIn();
    const results: unknown[] = [];
    client.get('/api/v1/lots').subscribe((r) => results.push(r));

    http.expectOne('/api/v1/lots').flush(null, unauthorized);
    http.expectOne('/api/v1/auth/refresh').flush(authResponse('new-token'));

    const replay = http.expectOne('/api/v1/lots');
    expect(replay.request.headers.get('Authorization')).toBe('Bearer new-token');
    replay.flush({ items: [] });

    expect(results).toEqual([{ items: [] }]);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('several requests failing at once trigger a single refresh, and all are replayed', () => {
    signIn();
    const results: string[] = [];
    client.get('/api/v1/lots').subscribe(() => results.push('lots'));
    client.get('/api/v1/vehicles').subscribe(() => results.push('vehicles'));
    client.get('/api/v1/pricing').subscribe(() => results.push('pricing'));

    for (const url of ['/api/v1/lots', '/api/v1/vehicles', '/api/v1/pricing'])
      http.expectOne(url).flush(null, unauthorized);
    http.expectOne('/api/v1/auth/refresh').flush(authResponse('new-token')); // exactly one

    for (const url of ['/api/v1/lots', '/api/v1/vehicles', '/api/v1/pricing']) {
      const replay = http.expectOne(url);
      expect(replay.request.headers.get('Authorization')).toBe('Bearer new-token');
      replay.flush({});
    }
    expect(results.sort()).toEqual(['lots', 'pricing', 'vehicles']);
  });

  it('if the refresh fails the session ends: user is cleared and sent to the login page, and the caller sees the error', () => {
    signIn();
    let failed = false;
    client.get('/api/v1/lots').subscribe({ error: () => (failed = true) });

    http.expectOne('/api/v1/lots').flush(null, unauthorized);
    http.expectOne('/api/v1/auth/refresh').flush(null, unauthorized);

    expect(failed).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/' } });
  });

  it('a failure of the REPLAYED request is just that request failing: the session stays', () => {
    signIn();
    let status = 0;
    client.get('/api/v1/lots').subscribe({ error: (e) => (status = e.status) });

    http.expectOne('/api/v1/lots').flush(null, unauthorized);
    http.expectOne('/api/v1/auth/refresh').flush(authResponse('new-token'));
    http.expectOne('/api/v1/lots').flush(null, { status: 500, statusText: 'Server Error' });

    expect(status).toBe(500);
    expect(auth.isAuthenticated()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not loop: a second 401 on the replay is returned to the caller without another refresh', () => {
    signIn();
    let status = 0;
    client.get('/api/v1/lots').subscribe({ error: (e) => (status = e.status) });

    http.expectOne('/api/v1/lots').flush(null, unauthorized);
    http.expectOne('/api/v1/auth/refresh').flush(authResponse('new-token'));
    http.expectOne('/api/v1/lots').flush(null, unauthorized);

    expect(status).toBe(401);
    http.expectNone('/api/v1/auth/refresh');
  });

  it('403 (signed in but not allowed) is not a reason to refresh', () => {
    signIn();
    let status = 0;
    client.get('/api/v1/users').subscribe({ error: (e) => (status = e.status) });
    http.expectOne('/api/v1/users').flush(null, { status: 403, statusText: 'Forbidden' });
    expect(status).toBe(403);
    http.expectNone('/api/v1/auth/refresh');
  });

  it('a 401 while already signed out does not try to refresh', () => {
    let status = 0;
    client.get('/api/v1/lots').subscribe({ error: (e) => (status = e.status) });
    http.expectOne('/api/v1/lots').flush(null, unauthorized);
    expect(status).toBe(401);
    http.expectNone('/api/v1/auth/refresh');
  });

  it('a look-alike path is not mistaken for an auth endpoint', () => {
    signIn();
    client.get('/api/v1/aaaa/login').subscribe({ error: () => undefined });
    http.expectOne('/api/v1/aaaa/login').flush(null, unauthorized);
    http.expectOne('/api/v1/auth/refresh').flush(null, unauthorized); // it WAS treated as a normal API call
  });
});
