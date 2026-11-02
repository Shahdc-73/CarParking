import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { toApiError } from '../../core/api/problem-details';
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

  protected readonly status = fillStatus;
  protected readonly percent = percentFull;
  protected readonly typeIcon = spotTypeIcon;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.parking.getOverview().subscribe({
      next: (lots) => this.state.set({ status: 'ready', lots, updatedAt: new Date() }),
      error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
    });
  }
}
