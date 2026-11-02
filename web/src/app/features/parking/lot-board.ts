import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { toApiError } from '../../core/api/problem-details';
import { PageHeader } from '../../shared/ui/page-header';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { formatElapsed, freeSecondsLeft, spokenDuration } from './elapsed';
import { spotTypeIcon } from './occupancy';
import { BoardSpot, LotBoard, SpotOccupancy, SpotStatus } from './parking.models';
import { ParkingService } from './parking.service';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; board: LotBoard; loadedAt: Date };

/** The words and icon of each status. Meaning never depends on colour alone. */
const STATUS_VIEW: Record<SpotStatus, { label: string; icon: string }> = {
  Available: { label: 'Free', icon: 'check_circle' },
  Occupied: { label: 'Occupied', icon: 'directions_car' },
  Reserved: { label: 'Reserved', icon: 'schedule' },
  OutOfService: { label: 'Out of service', icon: 'block' },
};

const TICK_MS = 1_000;
const REFRESH_MS = 30_000;

@Component({
  selector: 'app-lot-board',
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    PageHeader,
    Loading,
    ErrorBanner,
    EmptyState,
  ],
  templateUrl: './lot-board.html',
  styleUrl: './lot-board.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LotBoardPage {
  private readonly parking = inject(ParkingService);

  /** Bound from the route (/lots/:id) by withComponentInputBinding. */
  readonly id = input.required<string>();

  protected readonly state = signal<ViewState>({ status: 'loading' });
  protected readonly refreshing = signal(false);
  /** Set when a background refresh fails: we keep showing the last good data and say so. */
  protected readonly refreshError = signal<string | null>(null);

  /** Ticks every second; every timer on screen is computed from it, so one signal drives all of them. */
  private readonly now = signal(Date.now());
  /** Server clock minus browser clock at the last load. Timers use it, so a wrong PC clock cannot show a wrong stay. */
  private serverOffsetMs = 0;

  protected readonly viewOf = STATUS_VIEW;
  protected readonly typeIcon = spotTypeIcon;
  protected readonly formatElapsed = formatElapsed;
  protected readonly spoken = spokenDuration;

  constructor() {
    const tick = setInterval(() => this.now.set(Date.now()), TICK_MS);
    // Occupancy changes all day (and fees change on the hour), so the board refreshes itself. Not while the tab is hidden.
    const poll = setInterval(() => {
      if (!document.hidden) this.load(true);
    }, REFRESH_MS);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(tick);
      clearInterval(poll);
    });

    effect(() => {
      this.id(); // reload when navigating from one lot straight to another
      untracked(() => this.load(false));
    });
  }

  protected load(background: boolean): void {
    if (background) this.refreshing.set(true);
    else this.state.set({ status: 'loading' });

    this.parking.getBoard(this.id()).subscribe({
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

  /** Whole seconds this vehicle has been parked, by the SERVER's idea of "now". */
  protected elapsed(o: SpotOccupancy): number {
    return Math.max(
      0,
      Math.floor((this.now() + this.serverOffsetMs - Date.parse(o.checkInAtUtc)) / 1000),
    );
  }

  protected freeLeft(o: SpotOccupancy): number {
    return freeSecondsLeft(this.elapsed(o), o.freeMinutes);
  }

  protected ariaLabel(spot: BoardSpot): string {
    const base = `Spot ${spot.code}, ${spot.type}, ${STATUS_VIEW[spot.status].label.toLowerCase()}`;
    const o = spot.occupancy;
    if (!o) return base;
    const fee = o.feeSoFar === null ? '' : `, fee so far ${o.feeSoFar.toFixed(2)}`;
    return `${base}, parked for ${spokenDuration(this.elapsed(o))}${fee}`;
  }
}
