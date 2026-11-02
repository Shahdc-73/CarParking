import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { toApiError } from '../../core/api/problem-details';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { LotOverview } from './parking.models';
import { ParkingService } from './parking.service';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; lots: LotOverview[] };

@Component({
  selector: 'app-lots-overview',
  imports: [
    MatCardModule,
    MatIconModule,
    MatButtonModule,
    MatProgressBarModule,
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

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.parking.getOverview().subscribe({
      next: (lots) => this.state.set({ status: 'ready', lots }),
      error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
    });
  }

  /** Share of spots in use, 0-100, for the progress bar. */
  protected occupancy(o: LotOverview): number {
    const { total, available } = o.availability.totals;
    return total === 0 ? 0 : Math.round(((total - available) / total) * 100);
  }
}
