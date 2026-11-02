import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { toApiError } from '../../core/api/problem-details';
import { PricingApi } from '../../core/api/pricing.api';
import { SessionsApi } from '../../core/api/sessions.api';
import { newIdempotencyKey, parseTimeSpan } from '../../core/time';
import { FeePreview, SettlementResult } from '../../shared/models/session.models';
import { ErrorBanner, Loading } from '../../shared/ui/status';
import { formatElapsed } from '../parking/elapsed';
import { ReceiptView } from './receipt-view';

export type SettlementData =
  | { mode: 'check-out'; sessionId: string; plateNumber: string; spotCode: string }
  | { mode: 'lost-ticket'; plateNumber: string; spotCode: string };

type Step = 'loading' | 'review' | 'paying' | 'done' | 'error';

/**
 * Check-out (and lost ticket) in one dialog: review the fee, take the (mock) payment, then show the receipt.
 * The dialog closes with `true` when something was settled, so the page behind it refreshes.
 */
@Component({
  selector: 'app-settlement-dialog',
  imports: [
    DecimalPipe,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    ErrorBanner,
    Loading,
    ReceiptView,
  ],
  templateUrl: './settlement-dialog.html',
  styleUrl: './settlement-dialog.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettlementDialog implements OnInit {
  protected readonly data = inject<SettlementData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<SettlementDialog, boolean>>(MatDialogRef);
  private readonly sessions = inject(SessionsApi);
  private readonly pricing = inject(PricingApi);

  protected readonly step = signal<Step>('loading');
  protected readonly error = signal<string | null>(null);
  protected readonly preview = signal<FeePreview | null>(null);
  protected readonly lostTicketFee = signal<number | null>(null);
  protected readonly result = signal<SettlementResult | null>(null);

  /** One key for THIS payment attempt. If the network drops and the user presses the button again, the same key is re-sent, so the server cannot charge twice. */
  private readonly idempotencyKey = newIdempotencyKey();

  protected readonly formatElapsed = formatElapsed;
  protected readonly parseTimeSpan = parseTimeSpan;

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.step.set('loading');
    this.error.set(null);

    if (this.data.mode === 'check-out') {
      this.sessions.feePreview(this.data.sessionId).subscribe({
        next: (preview) => {
          this.preview.set(preview);
          this.step.set('review');
        },
        error: (e: unknown) => this.fail(e),
      });
    } else {
      this.pricing.current().subscribe({
        next: (pricing) => {
          this.lostTicketFee.set(pricing.lostTicketFee?.amount ?? null);
          this.step.set('review');
        },
        error: (e: unknown) => this.fail(e),
      });
    }
  }

  protected pay(): void {
    if (this.step() === 'paying') return; // ignore double clicks
    this.step.set('paying');
    this.error.set(null);

    const request =
      this.data.mode === 'check-out'
        ? this.sessions.checkOut(this.data.sessionId, this.idempotencyKey)
        : this.sessions.lostTicket(this.data.plateNumber);

    request.subscribe({
      next: (result) => {
        this.result.set(result);
        this.step.set('done');
      },
      error: (e: unknown) => {
        this.error.set(toApiError(e).message);
        this.step.set('review'); // back to review so the person can try again (same idempotency key)
      },
    });
  }

  protected print(): void {
    window.print();
  }

  protected close(): void {
    this.ref.close(this.step() === 'done');
  }

  private fail(e: unknown): void {
    this.error.set(toApiError(e).message);
    this.step.set('error');
  }
}
