import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { debounceTime } from 'rxjs';
import { toApiError } from '../../core/api/problem-details';
import { LiveUpdates } from '../../core/realtime/live-updates';
import { LiveBadge } from '../../shared/ui/live-badge';
import { PageHeader } from '../../shared/ui/page-header';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { fillStatus, percentFull, spotTypeIcon } from './occupancy';
import { LotOverview } from './parking.models';
import { ParkingService } from './parking.service';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; lots: LotOverview[]; updatedAt: Date };

@Component({
  selector: 'app-lots-overview',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    PageHeader,
    LiveBadge,
    Loading,
    ErrorBanner,
    EmptyState,
  ],
  templateUrl: './lots-overview.html',
  styleUrl: './lots-overview.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LotsOverview {
  private readonly parking = inject(ParkingService);
  private readonly live = inject(LiveUpdates);

  protected readonly state = signal<ViewState>({ status: 'loading' });

  /** The three numbers at the top. Closed lots are left out: their spots are not available to anyone. */
  protected readonly summary = computed(() => {
    const s = this.state();
    const open = s.status === 'ready' ? s.lots.filter((o) => o.lot.isActive) : [];
    return {
      freeNow: open.reduce((n, o) => n + o.availability.totals.available, 0),
      openLots: open.length,
      totalSpots: open.reduce((n, o) => n + o.availability.totals.total, 0),
    };
  });

  protected readonly mode = computed<'live' | 'reconnecting' | 'polling'>(() => {
    const state = this.live.state();
    return state === 'connected' ? 'live' : state === 'reconnecting' ? 'reconnecting' : 'polling';
  });

  protected readonly status = fillStatus;
  protected readonly percent = percentFull;
  protected readonly typeIcon = spotTypeIcon;

  constructor() {
    this.load(false);
    // Any lot changing means the totals change: reload quietly (one reload for a burst of changes).
    this.live
      .watchAll()
      .pipe(debounceTime(300), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.load(this.state().status === 'ready'));
  }

  protected load(background = false): void {
    if (!background) this.state.set({ status: 'loading' });
    this.parking.getOverview().subscribe({
      next: (lots) => this.state.set({ status: 'ready', lots, updatedAt: new Date() }),
      error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
    });
  }
}
