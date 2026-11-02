import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { UsersApi } from '../../core/api/admin.api';
import { PagedResult } from '../../core/api/paging';
import { toApiError } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import { Notify } from '../../core/ui/notify';
import { AdminUser } from '../../shared/models/admin.models';
import { Chip } from '../../shared/ui/chip';
import { Confirm } from '../../shared/ui/confirm-dialog';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { ROLE_OPTIONS, UserDialog } from './user-dialog';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; page: PagedResult<AdminUser> };

@Component({
  selector: 'app-admin-users',
  imports: [
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatPaginatorModule,
    Chip,
    Loading,
    ErrorBanner,
    EmptyState,
  ],
  templateUrl: './admin-users.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminUsers {
  private readonly api = inject(UsersApi);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(Confirm);
  private readonly notify = inject(Notify);
  /** An admin cannot deactivate themselves (the API refuses it too); the button is disabled with an explanation. */
  protected readonly me = inject(AuthService).user;

  protected readonly state = signal<ViewState>({ status: 'loading' });
  protected readonly search = signal('');
  protected readonly role = signal('');
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(10);
  protected readonly roles = ROLE_OPTIONS;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.api
      .list({
        search: this.search(),
        role: this.role(),
        sort: 'name',
        page: this.pageIndex() + 1,
        pageSize: this.pageSize(),
      })
      .subscribe({
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

  protected create(): void {
    this.dialog
      .open<UserDialog, void, boolean>(UserDialog, { width: '30rem', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((created) => {
        if (!created) return;
        this.notify.success('User created. Give them their email and temporary password.');
        this.load();
      });
  }

  protected toggle(user: AdminUser): void {
    const deactivating = user.isActive;
    this.confirm
      .ask({
        title: deactivating ? `Deactivate ${user.fullName}?` : `Reactivate ${user.fullName}?`,
        message: deactivating
          ? 'They will be signed out and will not be able to sign in again until reactivated.'
          : 'They will be able to sign in again.',
        confirmLabel: deactivating ? 'Deactivate' : 'Reactivate',
        destructive: deactivating,
      })
      .subscribe((ok) => {
        if (!ok) return;
        this.api.setActive(user.id, !deactivating).subscribe({
          next: () => {
            this.notify.success(
              deactivating ? `${user.fullName} deactivated.` : `${user.fullName} reactivated.`,
            );
            this.load();
          },
          error: (e: unknown) => this.notify.error(toApiError(e).message),
        });
      });
  }
}
