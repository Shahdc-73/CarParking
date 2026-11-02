import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { PagedResult } from '../../core/api/paging';
import { toApiError } from '../../core/api/problem-details';
import { ReservationsApi } from '../../core/api/reservations.api';
import { Notify } from '../../core/ui/notify';
import { Reservation, ReservationStatus } from '../../shared/models/reservation.models';
import { Chip } from '../../shared/ui/chip';
import { Confirm } from '../../shared/ui/confirm-dialog';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { ReservationDialog } from './reservation-dialog';
import { RESERVATION_STATUS } from './status-views';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; page: PagedResult<Reservation> };

@Component({
  selector: 'app-my-reservations',
  imports: [
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatSelectModule,
    MatPaginatorModule,
    Chip,
    Loading,
    ErrorBanner,
    EmptyState,
  ],
  templateUrl: './my-reservations.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyReservations {
  private readonly api = inject(ReservationsApi);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(Confirm);
  private readonly notify = inject(Notify);

  protected readonly state = signal<ViewState>({ status: 'loading' });
  protected readonly statusFilter = signal<ReservationStatus | ''>('');
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(10);

  protected readonly view = RESERVATION_STATUS;
  protected readonly statuses = Object.keys(RESERVATION_STATUS) as ReservationStatus[];

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.api
      .list({
        status: this.statusFilter(),
        sort: 'start_desc',
        page: this.pageIndex() + 1,
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (page) => this.state.set({ status: 'ready', page }),
        error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
      });
  }

  protected setFilter(value: ReservationStatus | ''): void {
    this.statusFilter.set(value);
    this.pageIndex.set(0);
    this.load();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  protected sameDay(a: string, b: string): boolean {
    return new Date(a).toDateString() === new Date(b).toDateString();
  }

  protected create(): void {
    this.dialog
      .open<ReservationDialog, void, Reservation>(ReservationDialog, {
        width: '30rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((created) => {
        if (!created) return;
        this.notify.success(`Reserved spot ${created.spotCode} at ${created.lotName}.`);
        this.pageIndex.set(0);
        this.load();
      });
  }

  protected cancel(r: Reservation): void {
    this.confirm
      .ask({
        title: 'Cancel this reservation?',
        message: `Spot ${r.spotCode} at ${r.lotName} will be released for other drivers.`,
        confirmLabel: 'Cancel reservation',
        destructive: true,
      })
      .subscribe((ok) => {
        if (!ok) return;
        this.api.cancel(r.id).subscribe({
          next: () => {
            this.notify.success('Reservation cancelled.');
            this.load();
          },
          error: (e: unknown) => this.notify.error(toApiError(e).message),
        });
      });
  }
}
