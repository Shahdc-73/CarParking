import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { FormsModule } from '@angular/forms';
import { toApiError } from '../../core/api/problem-details';
import { Notify } from '../../core/ui/notify';
import { PageHeader } from '../../shared/ui/page-header';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { Lot } from '../parking/parking.models';
import { ParkingService } from '../parking/parking.service';
import { CheckInCard } from './check-in-card';
import { ParkedCar, ParkedList } from './parked-list';
import { SettlementData, SettlementDialog } from './settlement-dialog';

const STORAGE_KEY = 'pw.gate.lot';

/** The attendant's workstation: pick the lot you are staffing, check cars in, and settle the ones leaving. */
@Component({
  selector: 'app-gate-home',
  imports: [
    FormsModule,
    MatFormFieldModule,
    MatSelectModule,
    PageHeader,
    Loading,
    ErrorBanner,
    EmptyState,
    CheckInCard,
    ParkedList,
  ],
  templateUrl: './gate-home.html',
  styleUrl: './gate-home.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GateHome {
  private readonly parking = inject(ParkingService);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(Notify);

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly lots = signal<Lot[]>([]);
  protected readonly lotId = signal<string | null>(null);
  protected readonly refreshKey = signal(0);

  protected readonly selected = computed(
    () => this.lots().find((l) => l.id === this.lotId()) ?? null,
  );

  constructor() {
    this.loadLots();
    effect(() => {
      const id = this.lotId();
      if (id) untracked(() => remember(id));
    });
  }

  protected loadLots(): void {
    this.loading.set(true);
    this.error.set(null);
    this.parking.getLots().subscribe({
      next: (lots) => {
        const open = lots.filter((l) => l.isActive);
        this.lots.set(open);
        // Resume the lot this attendant staffed last time, if it is still open.
        const saved = recall();
        this.lotId.set(open.find((l) => l.id === saved)?.id ?? open[0]?.id ?? null);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(toApiError(e).message);
        this.loading.set(false);
      },
    });
  }

  protected refresh(): void {
    this.refreshKey.update((n) => n + 1);
  }

  protected openCheckOut(car: ParkedCar): void {
    this.settle({
      mode: 'check-out',
      sessionId: car.sessionId,
      plateNumber: car.plateNumber,
      spotCode: car.spotCode,
    });
  }

  protected openLostTicket(car: ParkedCar): void {
    this.settle({ mode: 'lost-ticket', plateNumber: car.plateNumber, spotCode: car.spotCode });
  }

  private settle(data: SettlementData): void {
    this.dialog
      .open<SettlementDialog, SettlementData, boolean>(SettlementDialog, {
        data,
        width: '30rem',
        maxWidth: '95vw',
        autoFocus: false,
      })
      .afterClosed()
      .subscribe((changed) => {
        if (!changed) return;
        this.refresh();
        this.notify.success(
          data.mode === 'lost-ticket'
            ? `Lost ticket closed for ${data.plateNumber}.`
            : `${data.plateNumber} checked out.`,
        );
      });
  }
}

/** Remembering which lot you staff is a harmless convenience; storage can be blocked, so it must never be required. */
function remember(id: string): void {
  try {
    localStorage.setItem(STORAGE_KEY, id);
  } catch {
    /* ignore */
  }
}

function recall(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
