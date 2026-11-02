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
import { LiveBadge } from '../../shared/ui/live-badge';
import { PageHeader } from '../../shared/ui/page-header';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { LiveUpdates } from '../../core/realtime/live-updates';
import { BoardFeed } from './board-feed';
import { formatElapsed, freeSecondsLeft, spokenDuration } from './elapsed';
import { spotTypeIcon } from './occupancy';
import { SPOT_STATUS } from './spot-status';
import { BoardSpot, SpotOccupancy } from './parking.models';
import { ParkingService } from './parking.service';

@Component({
  selector: 'app-lot-board',
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    PageHeader,
    LiveBadge,
    Loading,
    ErrorBanner,
    EmptyState,
  ],
  templateUrl: './lot-board.html',
  styleUrl: './lot-board.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LotBoardPage {
  /** Bound from the route (/lots/:id) by withComponentInputBinding. */
  readonly id = input.required<string>();

  private readonly feed = new BoardFeed(
    inject(ParkingService),
    () => this.id(),
    inject(DestroyRef),
  );

  protected readonly state = this.feed.state;
  protected readonly refreshing = this.feed.refreshing;
  protected readonly refreshError = this.feed.refreshError;
  protected readonly mode = this.feed.mode;

  protected readonly viewOf = SPOT_STATUS;
  protected readonly typeIcon = spotTypeIcon;
  protected readonly formatElapsed = formatElapsed;
  protected readonly spoken = spokenDuration;

  constructor() {
    effect(() => {
      const id = this.id(); // reload when navigating from one lot straight to another
      untracked(() => {
        this.feed.watch(id);
        this.feed.load(false);
      });
    });
  }

  protected load(background: boolean): void {
    this.feed.load(background);
  }

  /** Whole seconds this vehicle has been parked, by the SERVER's idea of "now". */
  protected elapsed(o: SpotOccupancy): number {
    return this.feed.elapsed(o.checkInAtUtc);
  }

  protected freeLeft(o: SpotOccupancy): number {
    return freeSecondsLeft(this.elapsed(o), o.freeMinutes);
  }

  protected ariaLabel(spot: BoardSpot): string {
    const base = `Spot ${spot.code}, ${spot.type}, ${SPOT_STATUS[spot.status].label.toLowerCase()}`;
    const o = spot.occupancy;
    if (!o) return base;
    const fee = o.feeSoFar === null ? '' : `, fee so far ${o.feeSoFar.toFixed(2)}`;
    return `${base}, parked for ${spokenDuration(this.elapsed(o))}${fee}`;
  }
}
