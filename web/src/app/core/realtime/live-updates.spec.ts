import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { LiveUpdates, RESYNC } from './live-updates';
import { REALTIME_CONNECTION_FACTORY, RealtimeConnection } from './realtime-connection';

/** A connection the test controls: it decides when start() succeeds or fails and can fire server events. */
class FakeConnection implements RealtimeConnection {
  calls: string[] = [];
  startOutcome: 'ok' | 'fail' | 'hang' = 'ok';
  startError: unknown = new Error('network down');
  stopped = false;
  private lotChanged: (id: string) => void = () => undefined;
  private reconnecting: () => void = () => undefined;
  private reconnected: () => void = () => undefined;
  private closed: () => void = () => undefined;

  start = vi.fn(async () => {
    this.calls.push('start');
    if (this.startOutcome === 'fail') throw this.startError;
    if (this.startOutcome === 'hang') await new Promise(() => undefined);
  });
  stop = vi.fn(async () => {
    this.stopped = true;
  });
  invoke = vi.fn(async (method: string, ...args: string[]) => {
    this.calls.push([method, ...args].join(':'));
  }) as unknown as RealtimeConnection['invoke'];
  onLotChanged = (h: (id: string) => void) => (this.lotChanged = h);
  onReconnecting = (h: () => void) => (this.reconnecting = h);
  onReconnected = (h: () => void) => (this.reconnected = h);
  onClose = (h: () => void) => (this.closed = h);

  serverSays = (lotId: string) => this.lotChanged(lotId);
  dropConnection = () => this.reconnecting();
  recover = () => this.reconnected();
  giveUp = () => this.closed();
}

describe('LiveUpdates', () => {
  let connections: FakeConnection[];
  let signedIn: ReturnType<typeof signal<boolean>>;
  let refresh: ReturnType<typeof vi.fn>;
  let live: LiveUpdates;

  const settle = async () => {
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);
    TestBed.tick();
  };
  const current = () => connections.at(-1)!;

  beforeEach(() => {
    vi.useFakeTimers();
    connections = [];
    signedIn = signal(true);
    refresh = vi.fn().mockReturnValue(of({}));
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { isAuthenticated: signedIn.asReadonly(), refresh } },
        {
          provide: REALTIME_CONNECTION_FACTORY,
          useValue: () => connections.push(new FakeConnection()) && connections.at(-1),
        },
      ],
    });
    live = TestBed.inject(LiveUpdates);
  });
  afterEach(() => vi.useRealTimers());

  it('does not connect until something wants live updates', async () => {
    await settle();
    expect(connections.length).toBe(0);
    expect(live.state()).toBe('disconnected');
  });

  it('connects once for any number of watchers, then reports connected', async () => {
    live.watchLot('a').subscribe();
    live.watchLot('b').subscribe();
    live.watchAll().subscribe();
    expect(live.state()).toBe('connecting');
    await settle();

    expect(connections.length).toBe(1);
    expect(current().start).toHaveBeenCalledTimes(1);
    expect(live.state()).toBe('connected');
    expect(live.isLive()).toBe(true);
  });

  it('joins the groups of everything being watched as soon as it is connected', async () => {
    live.watchLot('a').subscribe();
    live.watchLot('b').subscribe();
    live.watchAll().subscribe();
    await settle();

    expect(current().calls).toEqual(['start', 'JoinLot:a', 'JoinLot:b', 'JoinAllLots']);
  });

  it('joins a lot immediately when it is watched after the connection is up, and only once per lot', async () => {
    live.watchLot('a').subscribe();
    await settle();

    live.watchLot('b').subscribe();
    live.watchLot('b').subscribe();
    await settle();

    expect(current().calls.filter((c) => c === 'JoinLot:b').length).toBe(1);
  });

  it('leaves a lot only when its LAST watcher is gone', async () => {
    const first = live.watchLot('a').subscribe();
    const second = live.watchLot('a').subscribe();
    live.watchLot('b').subscribe();
    await settle();

    first.unsubscribe();
    await settle();
    expect(current().calls).not.toContain('LeaveLot:a');

    second.unsubscribe();
    await settle();
    expect(current().calls).toContain('LeaveLot:a');
  });

  it('tells a lot watcher only about its own lot, and an all-lots watcher about every lot', async () => {
    const forA: unknown[] = [];
    const forAll: string[] = [];
    live.watchLot('a').subscribe(() => forA.push('x'));
    live.watchAll().subscribe((id) => forAll.push(id));
    await settle();
    forA.length = 0;
    forAll.length = 0; // ignore the resync that follows connecting

    current().serverSays('b');
    current().serverSays('a');

    expect(forA.length).toBe(1);
    expect(forAll).toEqual(['b', 'a']);
  });

  it('sends everyone a resync once connected, so a view loaded before the connection cannot miss changes', async () => {
    const heard: string[] = [];
    live.watchAll().subscribe((id) => heard.push(id));
    await settle();
    expect(heard).toEqual([RESYNC]);
  });

  it('shows "reconnecting", then re-joins every group (SignalR forgets them) and resyncs', async () => {
    const heard: string[] = [];
    live.watchLot('a').subscribe();
    live.watchAll().subscribe((id) => heard.push(id));
    await settle();
    heard.length = 0;
    current().calls.length = 0;

    current().dropConnection();
    expect(live.state()).toBe('reconnecting');

    current().recover();
    await settle();

    expect(live.state()).toBe('connected');
    expect(current().calls).toEqual(['JoinLot:a', 'JoinAllLots']);
    expect(heard).toEqual([RESYNC]);
  });

  it('retries a failed first connection with growing delays', async () => {
    const first = new FakeConnection();
    first.startOutcome = 'fail';
    connections.push(first);
    TestBed.resetTestingModule();
    // simpler: drive through the public API with a factory that fails the first three times
    let attempts = 0;
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { isAuthenticated: signedIn.asReadonly(), refresh } },
        {
          provide: REALTIME_CONNECTION_FACTORY,
          useValue: () => {
            const c = new FakeConnection();
            c.startOutcome = ++attempts <= 3 ? 'fail' : 'ok';
            connections.push(c);
            return c;
          },
        },
      ],
    });
    connections = [];
    const l = TestBed.inject(LiveUpdates);
    l.watchLot('a').subscribe();
    await settle();
    expect(l.state()).toBe('disconnected');
    expect(attempts).toBe(1);

    await vi.advanceTimersByTimeAsync(999);
    expect(attempts).toBe(1);
    await vi.advanceTimersByTimeAsync(1); // 1 s
    expect(attempts).toBe(2);
    await vi.advanceTimersByTimeAsync(3000); // then 3 s
    expect(attempts).toBe(3);
    await vi.advanceTimersByTimeAsync(8000); // then 8 s: this one succeeds
    expect(attempts).toBe(4);
    expect(l.state()).toBe('connected');
  });

  it('asks for a fresh access token when the server says 401, then tries again', async () => {
    TestBed.resetTestingModule();
    let n = 0;
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { isAuthenticated: signedIn.asReadonly(), refresh } },
        {
          provide: REALTIME_CONNECTION_FACTORY,
          useValue: () => {
            const c = new FakeConnection();
            if (++n === 1) {
              c.startOutcome = 'fail';
              c.startError = new Error('Failed to complete negotiation: 401 Unauthorized');
            }
            connections.push(c);
            return c;
          },
        },
      ],
    });
    const l = TestBed.inject(LiveUpdates);
    l.watchLot('a').subscribe();
    await settle();

    expect(refresh).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1000);
    expect(l.state()).toBe('connected');
  });

  it('starts over when SignalR gives up and closes the connection', async () => {
    live.watchLot('a').subscribe();
    await settle();
    const first = current();

    first.giveUp();
    expect(live.state()).toBe('disconnected');
    await vi.advanceTimersByTimeAsync(1000);

    expect(connections.length).toBe(2);
    expect(live.state()).toBe('connected');
  });

  it('closes the connection when the last watcher leaves', async () => {
    const sub = live.watchLot('a').subscribe();
    await settle();
    const c = current();

    sub.unsubscribe();
    await settle();

    expect(c.stop).toHaveBeenCalled();
    expect(live.state()).toBe('disconnected');
  });

  it('closes the connection on sign-out and reconnects after signing in again', async () => {
    live.watchLot('a').subscribe();
    await settle();
    const first = current();

    signedIn.set(false);
    await settle();
    expect(first.stop).toHaveBeenCalled();
    expect(live.state()).toBe('disconnected');

    signedIn.set(true);
    await settle();
    expect(connections.length).toBe(2);
    expect(live.state()).toBe('connected');
  });

  it('never connects for a signed-out user', async () => {
    signedIn.set(false);
    live.watchLot('a').subscribe();
    await settle();
    expect(connections.length).toBe(0);
  });

  it('does not use a connection that was stopped while it was still connecting', async () => {
    live.watchLot('a').subscribe().unsubscribe(); // watcher leaves immediately
    await settle();
    expect(live.state()).toBe('disconnected');
  });
});
