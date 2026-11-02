import { DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { SessionsApi } from '../../core/api/sessions.api';
import { toApiError } from '../../core/api/problem-details';
import { Notify } from '../../core/ui/notify';
import { LiveBadge } from '../../shared/ui/live-badge';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { LiveUpdates } from '../../core/realtime/live-updates';
import { BoardFeed } from '../parking/board-feed';
import { formatElapsed, freeSecondsLeft, spokenDuration } from '../parking/elapsed';
import { SpotOccupancy } from '../parking/parking.models';
import { ParkingService } from '../parking/parking.service';

export interface ParkedCar {
  sessionId: string;
  plateNumber: string;
  spotCode: string;
  occupancy: SpotOccupancy;
}

const TICKET_PATTERN = /^PW-[A-Z0-9]{6}$/i;

/** "Parked now": every occupied spot of the lot as a row with a live timer and the fee so far, plus search by plate or ticket. */
@Component({
  selector: 'app-parked-list',
  imports: [
    DecimalPipe,
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    LiveBadge,
    Loading,
    ErrorBanner,
    EmptyState,
  ],
  templateUrl: './parked-list.html',
  styleUrl: './parked-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParkedList {
  private readonly sessions = inject(SessionsApi);
  private readonly notify = inject(Notify);

  readonly lotId = input.required<string>();
  /** The page bumps this number after a check-in/out so the list reloads immediately instead of waiting for the 30 s refresh. */
  readonly refreshKey = input(0);

  readonly checkOut = output<ParkedCar>();
  readonly lostTicket = output<ParkedCar>();
  /** A ticket code typed into the search box resolved to this session. */
  readonly ticketFound = output<ParkedCar>();

  private readonly feed = new BoardFeed(
    inject(ParkingService),
    () => this.lotId(),
    inject(DestroyRef),
  );
  protected readonly state = this.feed.state;
  protected readonly refreshing = this.feed.refreshing;
  protected readonly refreshError = this.feed.refreshError;
  protected readonly mode = this.feed.mode;

  protected readonly query = signal('');
  protected readonly lookingUp = signal(false);

  protected readonly formatElapsed = formatElapsed;

  /** Every car in the lot, longest-parked first (they are the ones to look at). */
  private readonly cars = computed<ParkedCar[]>(() => {
    const s = this.state();
    if (s.status !== 'ready') return [];
    return s.board.levels
      .flatMap((l) => l.spots)
      .filter((spot) => spot.status === 'Occupied' && spot.occupancy !== null)
      .map((spot) => ({
        sessionId: spot.occupancy!.sessionId,
        plateNumber: spot.occupancy!.plateNumber,
        spotCode: spot.code,
        occupancy: spot.occupancy!,
      }))
      .sort((a, b) => a.occupancy.checkInAtUtc.localeCompare(b.occupancy.checkInAtUtc));
  });

  protected readonly visible = computed(() => {
    const wanted = this.query()
      .replace(/[^A-Za-z0-9]/g, '')
      .toUpperCase();
    return wanted
      ? this.cars().filter(
          (c) =>
            c.plateNumber.includes(wanted) ||
            c.spotCode
              .replace(/[^A-Za-z0-9]/g, '')
              .toUpperCase()
              .includes(wanted),
        )
      : this.cars();
  });

  protected readonly total = computed(() => this.cars().length);

  constructor() {
    effect(() => {
      const lotId = this.lotId();
      this.refreshKey();
      untracked(() => {
        this.feed.watch(lotId);
        this.feed.load(this.state().status === 'ready'); // keep the rows on screen while reloading
      });
    });
  }

  protected load(): void {
    this.feed.load(this.state().status === 'ready');
  }

  protected elapsed(c: ParkedCar): number {
    return this.feed.elapsed(c.occupancy.checkInAtUtc);
  }

  protected freeLeft(c: ParkedCar): number {
    return freeSecondsLeft(this.elapsed(c), c.occupancy.freeMinutes);
  }

  protected spoken(c: ParkedCar): string {
    return spokenDuration(this.elapsed(c));
  }

  /** Enter in the search box: a ticket code (PW-XXXXXX) is looked up exactly on the server; plates are filtered live. */
  protected search(): void {
    const q = this.query().trim();
    if (!TICKET_PATTERN.test(q)) return;

    this.lookingUp.set(true);
    this.sessions.list({ ticketCode: q.toUpperCase(), status: 'Active', pageSize: 1 }).subscribe({
      next: (page) => {
        this.lookingUp.set(false);
        const s = page.items[0];
        if (!s) return this.notify.error(`No parked vehicle has ticket ${q.toUpperCase()}.`);
        this.query.set(''); // the code did its job; leaving it would filter the list down to "No match"
        this.ticketFound.emit({
          sessionId: s.id,
          plateNumber: s.plateNumber,
          spotCode: s.spotCode,
          occupancy: {
            sessionId: s.id,
            checkInAtUtc: s.checkInAtUtc,
            plateNumber: s.plateNumber,
            feeSoFar: null,
            freeMinutes: null,
            isMine: false,
          },
        });
      },
      error: (e: unknown) => {
        this.lookingUp.set(false);
        this.notify.error(toApiError(e).message);
      },
    });
  }
}
