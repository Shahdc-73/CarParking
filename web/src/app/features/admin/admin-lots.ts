import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { RouterLink } from '@angular/router';
import { LotsAdminApi } from '../../core/api/admin.api';
import { PagedResult } from '../../core/api/paging';
import { toApiError } from '../../core/api/problem-details';
import { Notify } from '../../core/ui/notify';
import { Chip } from '../../shared/ui/chip';
import { Confirm } from '../../shared/ui/confirm-dialog';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { Lot } from '../parking/parking.models';
import { LotDialog } from './lot-dialog';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; page: PagedResult<Lot> };

@Component({
  selector: 'app-admin-lots',
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    Chip,
    Loading,
    ErrorBanner,
    EmptyState,
  ],
  templateUrl: './admin-lots.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminLots {
  private readonly api = inject(LotsAdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(Confirm);
  private readonly notify = inject(Notify);

  protected readonly state = signal<ViewState>({ status: 'loading' });
  protected readonly search = signal('');
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(10);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.api.list(this.search(), this.pageIndex() + 1, this.pageSize()).subscribe({
      next: (page) => this.state.set({ status: 'ready', page }),
      error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
    });
  }

  protected find(): void {
    this.pageIndex.set(0);
    this.load();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  protected openDialog(lot: Lot | null): void {
    this.dialog
      .open<LotDialog, Lot | null, boolean>(LotDialog, {
        data: lot,
        width: '28rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.notify.success(
          lot ? `${lot.name} updated.` : 'Lot created. Add levels and spots next.',
        );
        this.load();
      });
  }

  protected toggleActive(lot: Lot): void {
    const closing = lot.isActive;
    this.confirm
      .ask({
        title: closing ? `Close ${lot.name}?` : `Reopen ${lot.name}?`,
        message: closing
          ? 'Drivers and attendants will not be able to check in or reserve here. This is refused while cars are parked or reservations are pending.'
          : 'The lot will accept check-ins and reservations again.',
        confirmLabel: closing ? 'Close lot' : 'Reopen lot',
        destructive: closing,
      })
      .subscribe((ok) => {
        if (!ok) return;
        this.api.setActive(lot.id, !closing).subscribe({
          next: () => {
            this.notify.success(closing ? `${lot.name} closed.` : `${lot.name} reopened.`);
            this.load();
          },
          error: (e: unknown) => this.notify.error(toApiError(e).message),
        });
      });
  }

  protected remove(lot: Lot): void {
    this.confirm
      .ask({
        title: `Delete ${lot.name}?`,
        message:
          'This deletes the lot with all its levels and spots. A lot that has parking history cannot be deleted: close it instead.',
        confirmLabel: 'Delete lot',
        destructive: true,
      })
      .subscribe((ok) => {
        if (!ok) return;
        this.api.delete(lot.id).subscribe({
          next: () => {
            this.notify.success(`${lot.name} deleted.`);
            this.load();
          },
          error: (e: unknown) => this.notify.error(toApiError(e).message),
        });
      });
  }
}
