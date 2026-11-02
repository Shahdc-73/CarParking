import { DestroyRef, signal } from '@angular/core';
import { Subject, of } from 'rxjs';
import { vi } from 'vitest';
import { LiveUpdates } from '../../core/realtime/live-updates';
import { BoardFeed } from './board-feed';
import { LotBoard } from './parking.models';
import { ParkingService } from './parking.service';

const board = (): LotBoard => ({
  lotId: 'l1',
  lotName: 'L',
  isActive: true,
  generatedAtUtc: new Date().toISOString(),
  totals: { total: 0, available: 0, occupied: 0, reserved: 0, outOfService: 0 },
  levels: [],
});

describe('BoardFeed live behaviour', () => {
  let getBoard: ReturnType<typeof vi.fn>;
  let changes: Record<string, Subject<void>>;
  let state: ReturnType<
    typeof signal<'disconnected' | 'connecting' | 'connected' | 'reconnecting'>
  >;
  let destroy: () => void;
  let feed: BoardFeed;

  beforeEach(() => {
    vi.useFakeTimers();
    getBoard = vi.fn().mockImplementation(() => of(board()));
    changes = {};
    state = signal('connected');
    let destroyCallback: () => void = () => undefined;
    destroy = () => destroyCallback();
    const live = {
      state: state.asReadonly(),
      isLive: () => state() === 'connected',
      watchLot: (id: string) => (changes[id] ??= new Subject<void>()).asObservable(),
    } as unknown as LiveUpdates;
    feed = new BoardFeed(
      { getBoard } as unknown as ParkingService,
      () => 'l1',
      { onDestroy: (cb: () => void) => (destroyCallback = cb) } as unknown as DestroyRef,
      live,
    );
    feed.watch('l1');
    feed.load(false);
  });
  afterEach(() => vi.useRealTimers());

  it('reloads when the server says the lot changed', () => {
    expect(getBoard).toHaveBeenCalledTimes(1);
    changes['l1'].next();
    vi.advanceTimersByTime(300);
    expect(getBoard).toHaveBeenCalledTimes(2);
  });

  it('turns a burst of messages (several cars arriving together) into ONE reload', () => {
    for (let i = 0; i < 6; i++) {
      changes['l1'].next();
      vi.advanceTimersByTime(50);
    }
    vi.advanceTimersByTime(300);
    expect(getBoard).toHaveBeenCalledTimes(2); // the initial load + one
  });

  it('keeps the data on screen while a live reload is in flight (no spinner flash)', () => {
    changes['l1'].next();
    vi.advanceTimersByTime(300);
    expect(feed.state().status).toBe('ready');
  });

  it('only listens to the lot it was told to watch, and stops listening to the previous one', () => {
    feed.watch('l2');
    changes['l1'].next();
    vi.advanceTimersByTime(300);
    expect(getBoard).toHaveBeenCalledTimes(1);

    changes['l2'].next();
    vi.advanceTimersByTime(300);
    expect(getBoard).toHaveBeenCalledTimes(2);
  });

  it('while live, the poll is only a rare safety net (every 2 minutes)', () => {
    vi.advanceTimersByTime(90_000);
    expect(getBoard).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(30_000);
    expect(getBoard).toHaveBeenCalledTimes(2);
  });

  it('without the live channel it polls every 30 seconds', () => {
    state.set('disconnected');
    vi.advanceTimersByTime(30_000);
    expect(getBoard).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(30_000);
    expect(getBoard).toHaveBeenCalledTimes(3);
  });

  it('reports which mode it is in, for the badge', () => {
    expect(feed.mode()).toBe('live');
    state.set('reconnecting');
    expect(feed.mode()).toBe('reconnecting');
    state.set('connecting');
    expect(feed.mode()).toBe('polling');
    state.set('disconnected');
    expect(feed.mode()).toBe('polling');
  });

  it('stops listening and polling when its owner is destroyed', () => {
    destroy();
    changes['l1'].next();
    vi.advanceTimersByTime(300_000);
    expect(getBoard).toHaveBeenCalledTimes(1);
    expect(vi.getTimerCount()).toBe(0);
  });
});

describe('BoardFeed without a live channel at all', () => {
  it('still works and always reports polling', () => {
    vi.useFakeTimers();
    const getBoard = vi.fn().mockImplementation(() => of(board()));
    const feed = new BoardFeed({ getBoard } as unknown as ParkingService, () => 'l1', {
      onDestroy: () => undefined,
    } as unknown as DestroyRef);
    feed.watch('l1'); // harmless no-op
    feed.load(false);
    expect(feed.mode()).toBe('polling');
    vi.advanceTimersByTime(30_000);
    expect(getBoard).toHaveBeenCalledTimes(2);
    vi.useRealTimers();
  });
});
