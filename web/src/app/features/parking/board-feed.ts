import { DestroyRef, computed, signal } from '@angular/core';
import { Subscription, debounceTime } from 'rxjs';
import { toApiError } from '../../core/api/problem-details';
import { LiveUpdates } from '../../core/realtime/live-updates';
import { LotBoard } from './parking.models';
import { ParkingService } from './parking.service';

export type BoardState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; board: LotBoard; loadedAt: Date };

const TICK_MS = 1_000;
const REFRESH_MS = 30_000;
/** While the live channel is up the poll is only a safety net: it runs every 4th tick (2 minutes). */
const SAFETY_POLL_EVERY = 4;
/** A burst of changes (several cars arriving together) becomes ONE reload. */
const LIVE_DEBOUNCE_MS = 300;

/**
 * A live view of one lot: loads the board, reloads it the moment the server says the lot changed (SignalR), polls as a fallback
 * (every 30 s, not while the tab is hidden, and rarely while the live channel is up), and provides second-by-second
 * elapsed times that are correct even if this computer's clock is wrong (they are measured against the SERVER's clock).
 * Shared by the lot board page and the gate's "parked now" list. Timers are removed when the owning component is destroyed.
 */
export class BoardFeed {
  readonly state = signal<BoardState>({ status: 'loading' });
  readonly refreshing = signal(false);
  /** Set when a background refresh fails: the last good data stays on screen and this says why it may be stale. */
  readonly refreshError = signal<string | null>(null);

  /** Ticks every second; every timer on screen is computed from it, so one signal drives them all. */
  private readonly now = signal(Date.now());
  /** Server clock minus browser clock at the last load. */
  private serverOffsetMs = 0;

  private watching?: Subscription;
  private ticksSincePoll = 0;

  /** What the UI should say: "Live", "Reconnecting...", or "Updates every 30 seconds" when the channel is not available. */
  readonly mode = computed<'live' | 'reconnecting' | 'polling'>(() => {
    const state = this.live?.state();
    return state === 'connected' ? 'live' : state === 'reconnecting' ? 'reconnecting' : 'polling';
  });

  constructor(
    private readonly parking: ParkingService,
    private readonly lotId: () => string,
    destroyRef: DestroyRef,
    private readonly live?: LiveUpdates,
  ) {
    const tick = setInterval(() => this.now.set(Date.now()), TICK_MS);
    // Fees change on the hour and clocks drift, so the board also refreshes itself even if no message arrives.
    const poll = setInterval(() => {
      if (document.hidden) return;
      if (this.live?.isLive() && ++this.ticksSincePoll < SAFETY_POLL_EVERY) return;
      this.ticksSincePoll = 0;
      this.load(true);
    }, REFRESH_MS);
    destroyRef.onDestroy(() => {
      clearInterval(tick);
      clearInterval(poll);
      this.watching?.unsubscribe();
    });
  }

  /** (Re)starts listening for changes of the given lot. Call it whenever the lot being shown changes. */
  watch(lotId: string): void {
    this.watching?.unsubscribe();
    this.watching = this.live
      ?.watchLot(lotId)
      .pipe(debounceTime(LIVE_DEBOUNCE_MS))
      .subscribe(() => this.load(this.state().status === 'ready'));
  }

  load(background: boolean): void {
    if (background) this.refreshing.set(true);
    else this.state.set({ status: 'loading' });

    this.parking.getBoard(this.lotId()).subscribe({
      next: (board) => {
        this.serverOffsetMs = Date.parse(board.generatedAtUtc) - Date.now();
        this.state.set({ status: 'ready', board, loadedAt: new Date() });
        this.refreshError.set(null);
        this.refreshing.set(false);
      },
      error: (e: unknown) => {
        this.refreshing.set(false);
        const message = toApiError(e).message;
        if (background && this.state().status === 'ready') this.refreshError.set(message);
        else this.state.set({ status: 'error', message });
      },
    });
  }

  /** Whole seconds since `checkInAtUtc`, by the SERVER's idea of "now". Reads the tick signal, so templates update every second. */
  elapsed(checkInAtUtc: string): number {
    return Math.max(
      0,
      Math.floor((this.now() + this.serverOffsetMs - Date.parse(checkInAtUtc)) / 1000),
    );
  }
}
