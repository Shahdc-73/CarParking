import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { AuditApi } from '../../core/api/admin.api';
import { PagedResult } from '../../core/api/paging';
import { toApiError } from '../../core/api/problem-details';
import { localDateEndToUtcIso, localDateStartToUtcIso } from '../../core/time';
import { AuditLog } from '../../shared/models/admin.models';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import {
  AUDIT_ACTIONS,
  AUDIT_ENTITY_TYPES,
  humanizeAction,
  prettyJson,
  shortId,
} from './audit-views';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; page: PagedResult<AuditLog> };

@Component({
  selector: 'app-admin-audit',
  imports: [
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatPaginatorModule,
    Loading,
    ErrorBanner,
    EmptyState,
  ],
  templateUrl: './admin-audit.html',
  styleUrl: './admin-audit.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminAudit {
  private readonly api = inject(AuditApi);

  protected readonly state = signal<ViewState>({ status: 'loading' });
  protected readonly entityType = signal('');
  protected readonly action = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(25);

  protected readonly entityTypes = AUDIT_ENTITY_TYPES;
  protected readonly actions = AUDIT_ACTIONS;
  protected readonly humanize = humanizeAction;
  protected readonly pretty = prettyJson;
  protected readonly short = shortId;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.api
      .list({
        entityType: this.entityType(),
        action: this.action(),
        // Dates are the admin's LOCAL days; the API compares UTC instants.
        fromUtc: this.from() ? localDateStartToUtcIso(this.from()) : '',
        toUtc: this.to() ? localDateEndToUtcIso(this.to()) : '',
        page: this.pageIndex() + 1,
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (page) => this.state.set({ status: 'ready', page }),
        error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
      });
  }

  protected apply(): void {
    this.pageIndex.set(0);
    this.load();
  }

  protected reset(): void {
    this.entityType.set('');
    this.action.set('');
    this.from.set('');
    this.to.set('');
    this.apply();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.load();
  }
}
