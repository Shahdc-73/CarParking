import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { LotsAdminApi } from '../../core/api/admin.api';
import { toApiError } from '../../core/api/problem-details';
import { Notify } from '../../core/ui/notify';
import { LotDetail } from '../../shared/models/admin.models';
import { Chip } from '../../shared/ui/chip';
import { Confirm } from '../../shared/ui/confirm-dialog';
import { PageHeader } from '../../shared/ui/page-header';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { spotTypeIcon } from '../parking/occupancy';
import { BoardSpot, LotBoard } from '../parking/parking.models';
import { ParkingService } from '../parking/parking.service';
import { SPOT_STATUS } from '../parking/spot-status';
import { LevelDialog, LevelDialogData } from './level-dialog';
import { LotDialog } from './lot-dialog';
import { SpotDialog, SpotDialogData } from './spot-dialog';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; detail: LotDetail; board: LotBoard };

/** Everything about one lot: edit it, add levels, add/edit/retire spots. The spot list comes from the live board, so statuses are current. */
@Component({
  selector: 'app-admin-lot-detail',
  imports: [
    RouterLink,
    MatButtonModule,
    MatIconModule,
    Chip,
    PageHeader,
    Loading,
    ErrorBanner,
    EmptyState,
  ],
  templateUrl: './admin-lot-detail.html',
  styleUrl: './admin-lot-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminLotDetail {
  private readonly api = inject(LotsAdminApi);
  private readonly parking = inject(ParkingService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(Confirm);
  private readonly notify = inject(Notify);

  /** Bound from the route (/admin/lots/:id). */
  readonly id = input.required<string>();

  protected readonly state = signal<ViewState>({ status: 'loading' });
  protected readonly statusView = SPOT_STATUS;
  protected readonly typeIcon = spotTypeIcon;

  protected readonly nextLevelNumber = computed(() => {
    const s = this.state();
    return s.status === 'ready' ? Math.max(0, ...s.detail.levels.map((l) => l.number)) + 1 : 1;
  });

  constructor() {
    effect(() => {
      this.id();
      untracked(() => this.load());
    });
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    forkJoin({
      detail: this.api.get(this.id()),
      board: this.parking.getBoard(this.id()),
    }).subscribe({
      next: ({ detail, board }) => this.state.set({ status: 'ready', detail, board }),
      error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
    });
  }

  protected spotsOf(board: LotBoard, levelId: string): BoardSpot[] {
    return board.levels.find((l) => l.id === levelId)?.spots ?? [];
  }

  /** Runs a change, then reloads and says what happened (or why it was refused). */
  private run(
    action: { subscribe: (o: { next: () => void; error: (e: unknown) => void }) => unknown },
    success: string,
  ): void {
    action.subscribe({
      next: () => {
        this.notify.success(success);
        this.load();
      },
      error: (e: unknown) => this.notify.error(toApiError(e).message),
    });
  }

  private afterDialog(
    ref: { afterClosed: () => { subscribe: (f: (saved: boolean | undefined) => void) => unknown } },
    success: string,
  ): void {
    ref.afterClosed().subscribe((saved) => {
      if (!saved) return;
      this.notify.success(success);
      this.load();
    });
  }

  protected editLot(detail: LotDetail): void {
    this.afterDialog(
      this.dialog.open(LotDialog, { data: detail, width: '28rem', maxWidth: '95vw' }),
      `${detail.name} updated.`,
    );
  }

  protected addLevel(detail: LotDetail): void {
    const data: LevelDialogData = {
      mode: 'add',
      lotId: detail.id,
      nextNumber: this.nextLevelNumber(),
    };
    this.afterDialog(
      this.dialog.open(LevelDialog, { data, width: '26rem', maxWidth: '95vw' }),
      'Level added.',
    );
  }

  protected renameLevel(level: LotDetail['levels'][number]): void {
    this.afterDialog(
      this.dialog.open(LevelDialog, {
        data: { mode: 'rename', level } satisfies LevelDialogData,
        width: '26rem',
        maxWidth: '95vw',
      }),
      'Level renamed.',
    );
  }

  protected deleteLevel(level: LotDetail['levels'][number]): void {
    this.confirm
      .ask({
        title: `Delete level "${level.name}"?`,
        message:
          'All its spots are deleted too. A level that has parking history cannot be deleted.',
        confirmLabel: 'Delete level',
        destructive: true,
      })
      .subscribe(
        (ok) => ok && this.run(this.api.deleteLevel(level.id), `Level "${level.name}" deleted.`),
      );
  }

  protected addSpot(level: LotDetail['levels'][number]): void {
    const data: SpotDialogData = { mode: 'add', levelId: level.id, levelName: level.name };
    this.afterDialog(
      this.dialog.open(SpotDialog, { data, width: '26rem', maxWidth: '95vw' }),
      'Spot added.',
    );
  }

  protected editSpot(spot: BoardSpot): void {
    const data: SpotDialogData = {
      mode: 'edit',
      spotId: spot.id,
      code: spot.code,
      type: spot.type,
    };
    this.afterDialog(
      this.dialog.open(SpotDialog, { data, width: '26rem', maxWidth: '95vw' }),
      `Spot ${spot.code} updated.`,
    );
  }

  protected toggleService(spot: BoardSpot): void {
    const returning = spot.status === 'OutOfService';
    this.run(
      this.api.setSpotService(spot.id, returning),
      returning ? `Spot ${spot.code} is back in service.` : `Spot ${spot.code} is out of service.`,
    );
  }

  protected deleteSpot(spot: BoardSpot): void {
    this.confirm
      .ask({
        title: `Delete spot ${spot.code}?`,
        message:
          'A spot that has parking history cannot be deleted: take it out of service instead.',
        confirmLabel: 'Delete spot',
        destructive: true,
      })
      .subscribe(
        (ok) => ok && this.run(this.api.deleteSpot(spot.id), `Spot ${spot.code} deleted.`),
      );
  }
}
