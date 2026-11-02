import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { PricingApi } from '../../core/api/pricing.api';
import { toApiError } from '../../core/api/problem-details';
import { Notify } from '../../core/ui/notify';
import { PricingOverview } from '../../shared/models/pricing.models';
import { ErrorBanner, Loading } from '../../shared/ui/status';
import { spotTypeIcon } from '../parking/occupancy';
import { PricingDialog, PricingDialogData } from './pricing-dialog';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; pricing: PricingOverview };

@Component({
  selector: 'app-admin-pricing',
  imports: [DatePipe, DecimalPipe, MatButtonModule, MatIconModule, Loading, ErrorBanner],
  templateUrl: './admin-pricing.html',
  styleUrl: './admin-pricing.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPricing {
  private readonly api = inject(PricingApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(Notify);

  protected readonly state = signal<ViewState>({ status: 'loading' });
  protected readonly typeIcon = spotTypeIcon;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.api.current().subscribe({
      next: (pricing) => this.state.set({ status: 'ready', pricing }),
      error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
    });
  }

  protected edit(data: PricingDialogData): void {
    this.dialog
      .open<PricingDialog, PricingDialogData, boolean>(PricingDialog, {
        data,
        width: '28rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.notify.success('Price saved.');
        this.load();
      });
  }
}
