import { DestroyRef, signal } from '@angular/core';
import { toApiError } from '../../core/api/problem-details';
import { LotBoard } from './parking.models';
import { ParkingService } from './parking.service';

export type BoardState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; board: LotBoard; loadedAt: Date };

const TICK_MS = 1_000;
const REFRESH_MS = 30_000;

/**
 * A live view of one lot: loads the board, refreshes it every 30 s (not while the tab is hidden), and provides second-by-second
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

  constructor(
    private readonly parking: ParkingService,
    private readonly lotId: () => string,
    destroyRef: DestroyRef,
  ) {
    const tick = setInterval(() => this.now.set(Date.now()), TICK_MS);
    // Occupancy changes all day and fees change on the hour, so the board keeps itself fresh.
    const poll = setInterval(() => {
      if (!document.hidden) this.load(true);
    }, REFRESH_MS);
    destroyRef.onDestroy(() => {
      clearInterval(tick);
      clearInterval(poll);
    });
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
