import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, firstValueFrom, map, of, shareReplay, tap } from 'rxjs';
import { API_BASE_URL } from '../api/api.config';
import { AuthResponse, LoginRequest, RegisterRequest, Role, User } from './auth.models';

/**
 * Owns the signed-in session.
 *
 * Token strategy (the "why" for interviews):
 *  - The ACCESS token (short-lived JWT) lives only in memory here. JavaScript can read it, so it must be short-lived
 *    and must never be put in localStorage, where any XSS bug could steal it for good.
 *  - The REFRESH token is an HttpOnly cookie set by the API. JavaScript cannot read it at all; the browser just sends it
 *    to /auth/refresh. After a page reload the access token is gone, so the app silently calls refresh to get a new one.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/auth`;

  private accessToken: string | null = null;
  private refreshInFlight?: Observable<AuthResponse>;

  private readonly _user = signal<User | null>(null);
  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);

  token(): string | null {
    return this.accessToken;
  }

  hasAnyRole(roles: readonly Role[]): boolean {
    const mine = this._user()?.roles ?? [];
    return roles.some((r) => mine.includes(r));
  }

  login(request: LoginRequest): Observable<User> {
    return this.http
      .post<AuthResponse>(`${this.base}/login`, request, { withCredentials: true })
      .pipe(
        tap((r) => this.setSession(r)),
        map((r) => r.user),
      );
  }

  register(request: RegisterRequest): Observable<User> {
    return this.http
      .post<AuthResponse>(`${this.base}/register`, request, { withCredentials: true })
      .pipe(
        tap((r) => this.setSession(r)),
        map((r) => r.user),
      );
  }

  /** Always clears the local session, even if the server call fails (e.g. offline): the user asked to sign out. */
  logout(): Observable<void> {
    return this.http.post<void>(`${this.base}/logout`, {}, { withCredentials: true }).pipe(
      catchError(() => of(undefined)),
      tap(() => this.clearSession()),
      map(() => undefined),
    );
  }

  /**
   * Exchanges the refresh cookie for a new access token.
   *
   * SINGLE-FLIGHT: the API rotates refresh tokens, and reusing an already-used one is treated as theft (it revokes the
   * whole session). If three requests get a 401 at once and each refreshed on its own, two would look like a replay.
   * So concurrent callers share ONE in-flight request.
   */
  refresh(): Observable<AuthResponse> {
    this.refreshInFlight ??= this.http
      .post<AuthResponse>(`${this.base}/refresh`, {}, { withCredentials: true })
      .pipe(
        tap((r) => this.setSession(r)),
        finalize(() => (this.refreshInFlight = undefined)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    return this.refreshInFlight;
  }

  /** Called once at startup. Success means "you are still signed in"; failure is normal ("please sign in"). */
  async restoreSession(): Promise<void> {
    try {
      await firstValueFrom(this.refresh());
    } catch {
      this.clearSession();
    }
  }

  clearSession(): void {
    this.accessToken = null;
    this._user.set(null);
  }

  private setSession(response: AuthResponse): void {
    this.accessToken = response.accessToken;
    this._user.set(response.user);
  }
}
