import { Injectable, Injector, computed, effect, inject, signal, untracked } from '@angular/core';
import { Observable } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { REALTIME_CONNECTION_FACTORY, RealtimeConnection } from './realtime-connection';

export type LiveState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/** Emitted to every watcher after a (re)connection: messages may have been missed meanwhile, so re-read everything. */
export const RESYNC = '*';

/** Delays between attempts to open the FIRST connection (SignalR's own reconnect only starts after one has succeeded). */
const START_RETRY_MS = [1000, 3000, 8000, 20_000];

/**
 * One shared real-time connection for the whole app.
 *
 * Views do not care about sockets: `watchLot(id)` / `watchAll()` return an Observable that fires when something changed. The
 * message carries NO data (see AvailabilityHub), so a view simply re-fetches through the normal, authorised API.
 *
 *  - The connection opens when the first view starts watching and the user is signed in, and closes when nobody watches or the
 *    user signs out. Group membership is reference-counted.
 *  - SignalR forgets group membership when a connection drops, so every group is re-joined after a reconnect, and watchers get
 *    a RESYNC to catch up on anything missed.
 *  - If the connection cannot be made, views keep working through their own polling; `state` tells the UI which mode is active.
 */
@Injectable({ providedIn: 'root' })
export class LiveUpdates {
  private readonly factory = inject(REALTIME_CONNECTION_FACTORY);
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);

  private readonly _state = signal<LiveState>('disconnected');
  readonly state = this._state.asReadonly();
  readonly isLive = computed(() => this._state() === 'connected');

  private connection?: RealtimeConnection;
  private starting = false;
  private startAttempt = 0;
  private retryTimer?: ReturnType<typeof setTimeout>;

  private readonly lotWatchers = new Map<string, number>();
  private allWatchers = 0;
  private readonly listeners = new Set<(lotId: string) => void>();

  constructor() {
    // Signing out must close the socket: it was authenticated as that user.
    effect(() => {
      const signedIn = this.auth.isAuthenticated();
      untracked(() => (signedIn ? this.ensureStarted() : void this.shutdown()));
    });
  }

  /** Fires (with no value) when the given lot changed, and after every resync. */
  watchLot(lotId: string): Observable<void> {
    return new Observable<void>((subscriber) => {
      const listener = (changed: string) =>
        (changed === lotId || changed === RESYNC) && subscriber.next();
      this.listeners.add(listener);
      this.lotWatchers.set(lotId, (this.lotWatchers.get(lotId) ?? 0) + 1);
      if (this.lotWatchers.get(lotId) === 1) void this.invokeIfConnected('JoinLot', lotId);
      this.ensureStarted();

      return () => {
        this.listeners.delete(listener);
        const left = (this.lotWatchers.get(lotId) ?? 1) - 1;
        if (left <= 0) {
          this.lotWatchers.delete(lotId);
          void this.invokeIfConnected('LeaveLot', lotId);
        } else this.lotWatchers.set(lotId, left);
        this.stopIfIdle();
      };
    });
  }

  /** Fires with the id of any lot that changed (or RESYNC). For overview screens that show every lot. */
  watchAll(): Observable<string> {
    return new Observable<string>((subscriber) => {
      const listener = (changed: string) => subscriber.next(changed);
      this.listeners.add(listener);
      this.allWatchers++;
      if (this.allWatchers === 1) void this.invokeIfConnected('JoinAllLots');
      this.ensureStarted();

      return () => {
        this.listeners.delete(listener);
        this.allWatchers--;
        if (this.allWatchers === 0) void this.invokeIfConnected('LeaveAllLots');
        this.stopIfIdle();
      };
    });
  }

  private get hasWatchers(): boolean {
    return this.lotWatchers.size > 0 || this.allWatchers > 0;
  }

  private ensureStarted(): void {
    if (this.connection || this.starting || !this.hasWatchers || !this.auth.isAuthenticated())
      return;
    this.starting = true;
    this._state.set('connecting');

    const connection = this.factory();
    this.connection = connection;
    connection.onLotChanged((lotId) => this.emit(lotId));
    connection.onReconnecting(() => this._state.set('reconnecting'));
    connection.onReconnected(() => void this.joinAllGroups().then(() => this.afterConnected()));
    connection.onClose(() => {
      // SignalR gave up (or we stopped). Unless we stopped on purpose, start over.
      if (this.connection === connection) {
        this.connection = undefined;
        this._state.set('disconnected');
        this.scheduleRestart();
      }
    });
    void this.start(connection);
  }

  private async start(connection: RealtimeConnection): Promise<void> {
    try {
      await connection.start();
      if (this.connection !== connection) return void connection.stop(); // stopped while connecting
      this.starting = false;
      await this.joinAllGroups();
      this.afterConnected();
    } catch (error) {
      this.starting = false;
      if (this.connection !== connection) return;
      this.connection = undefined;
      this._state.set('disconnected');
      // A 401 means the access token expired: get a fresh one (the HttpOnly cookie is still valid) before trying again.
      if (String(error).includes('401') && this.auth.isAuthenticated())
        this.auth.refresh().subscribe({ error: () => undefined });
      this.scheduleRestart();
    }
  }

  private afterConnected(): void {
    this.startAttempt = 0;
    this._state.set('connected');
    this.emit(RESYNC);
  }

  private async joinAllGroups(): Promise<void> {
    const connection = this.connection;
    if (!connection) return;
    for (const lotId of this.lotWatchers.keys()) await connection.invoke('JoinLot', lotId);
    if (this.allWatchers > 0) await connection.invoke('JoinAllLots');
  }

  private async invokeIfConnected(
    method: 'JoinLot' | 'LeaveLot' | 'JoinAllLots' | 'LeaveAllLots',
    lotId?: string,
  ): Promise<void> {
    if (this._state() !== 'connected' || !this.connection) return; // joined on connect instead
    try {
      if (lotId === undefined)
        await this.connection.invoke(method as 'JoinAllLots' | 'LeaveAllLots');
      else await this.connection.invoke(method as 'JoinLot' | 'LeaveLot', lotId);
    } catch {
      /* the next reconnect re-joins everything */
    }
  }

  private emit(lotId: string): void {
    for (const listener of [...this.listeners]) listener(lotId);
  }

  private scheduleRestart(): void {
    clearTimeout(this.retryTimer);
    if (!this.hasWatchers || !this.auth.isAuthenticated()) return;
    const delay = START_RETRY_MS[Math.min(this.startAttempt++, START_RETRY_MS.length - 1)];
    this.retryTimer = setTimeout(() => this.ensureStarted(), delay);
  }

  private stopIfIdle(): void {
    if (!this.hasWatchers) void this.shutdown();
  }

  private async shutdown(): Promise<void> {
    clearTimeout(this.retryTimer);
    const connection = this.connection;
    this.connection = undefined;
    this.starting = false;
    this.startAttempt = 0;
    this._state.set('disconnected');
    if (connection) await connection.stop().catch(() => undefined);
  }
}
