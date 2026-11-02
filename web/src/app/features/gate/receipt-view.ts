import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { formatElapsed } from '../parking/elapsed';
import { parseTimeSpan } from '../../core/time';
import { Receipt } from '../../shared/models/session.models';

/** A receipt as a printable slip. Used by the gate (after payment) and by a driver's history. */
@Component({
  selector: 'app-receipt-view',
  imports: [DatePipe, DecimalPipe, MatIconModule],
  templateUrl: './receipt-view.html',
  styleUrl: './receipt-view.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptView {
  readonly receipt = input.required<Receipt>();

  protected readonly duration = computed(() =>
    formatElapsed(parseTimeSpan(this.receipt().breakdown.duration)),
  );
}
