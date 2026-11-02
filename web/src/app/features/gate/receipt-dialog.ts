import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { toApiError } from '../../core/api/problem-details';
import { SessionsApi } from '../../core/api/sessions.api';
import { Receipt } from '../../shared/models/session.models';
import { ErrorBanner, Loading } from '../../shared/ui/status';
import { ReceiptView } from './receipt-view';

/** Opens an existing receipt by id (e.g. from a driver's history) and lets it be printed. */
@Component({
  selector: 'app-receipt-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule, Loading, ErrorBanner, ReceiptView],
  template: `
    <h2 mat-dialog-title>Receipt</h2>
    <mat-dialog-content>
      @if (error(); as message) {
        <app-error-banner [message]="message" (retry)="load()" />
      } @else if (receipt(); as r) {
        <app-receipt-view [receipt]="r" />
      } @else {
        <app-loading label="Loading receipt" />
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (receipt()) {
        <button mat-button type="button" (click)="print()">
          <mat-icon aria-hidden="true">print</mat-icon>
          Print
        </button>
      }
      <button mat-flat-button type="button" mat-dialog-close cdkFocusInitial>Close</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptDialog implements OnInit {
  private readonly data = inject<{ receiptId: string }>(MAT_DIALOG_DATA);
  private readonly sessions = inject(SessionsApi);

  protected readonly receipt = signal<Receipt | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.error.set(null);
    this.sessions.receipt(this.data.receiptId).subscribe({
      next: (r) => this.receipt.set(r),
      error: (e: unknown) => this.error.set(toApiError(e).message),
    });
  }

  protected print(): void {
    window.print();
  }
}
