import { DatePipe, DecimalPipe } from '@angular/common';
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
import { SessionsApi } from '../../core/api/sessions.api';
import { Session, SessionStatus } from '../../shared/models/session.models';
import { Chip } from '../../shared/ui/chip';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { ReceiptDialog } from '../gate/receipt-dialog';
import { formatElapsed } from '../parking/elapsed';
import { SESSION_STATUS } from '../reservations/status-views';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; page: PagedResult<Session> };

@Component({
  selector: 'app-my-history',
  imports: [
    DatePipe,
    DecimalPipe,
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
  templateUrl: './my-history.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyHistory {
  private readonly api = inject(SessionsApi);
  private readonly dialog = inject(MatDialog);

  protected readonly state = signal<ViewState>({ status: 'loading' });
  protected readonly statusFilter = signal<SessionStatus | ''>('');
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(10);

  protected readonly view = SESSION_STATUS;
  protected readonly statuses = Object.keys(SESSION_STATUS) as SessionStatus[];

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.api
      .list({ status: this.statusFilter(), page: this.pageIndex() + 1, pageSize: this.pageSize() })
      .subscribe({
        next: (page) => this.state.set({ status: 'ready', page }),
        error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
      });
  }

  protected setFilter(value: SessionStatus | ''): void {
    this.statusFilter.set(value);
    this.pageIndex.set(0);
    this.load();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  protected duration(s: Session): string {
    return s.checkOutAtUtc
      ? formatElapsed((Date.parse(s.checkOutAtUtc) - Date.parse(s.checkInAtUtc)) / 1000)
      : '';
  }

  protected openReceipt(s: Session): void {
    if (!s.receiptId) return;
    this.dialog.open(ReceiptDialog, {
      data: { receiptId: s.receiptId },
      width: '30rem',
      maxWidth: '95vw',
      autoFocus: false,
    });
  }
}
