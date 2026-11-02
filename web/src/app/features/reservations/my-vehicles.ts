import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { toApiError } from '../../core/api/problem-details';
import { VehiclesApi } from '../../core/api/vehicles.api';
import { Notify } from '../../core/ui/notify';
import { VEHICLE_TYPES, Vehicle, vehicleTypeLabel } from '../../shared/models/vehicle.models';
import { Confirm } from '../../shared/ui/confirm-dialog';
import { Chip } from '../../shared/ui/chip';
import { EmptyState, ErrorBanner, Loading } from '../../shared/ui/status';
import { VehicleDialog } from './vehicle-dialog';

type ViewState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; vehicles: Vehicle[] };

@Component({
  selector: 'app-my-vehicles',
  imports: [MatButtonModule, MatIconModule, Chip, Loading, ErrorBanner, EmptyState],
  templateUrl: './my-vehicles.html',
  styleUrl: './my-vehicles.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyVehicles {
  private readonly vehiclesApi = inject(VehiclesApi);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(Confirm);
  private readonly notify = inject(Notify);

  protected readonly state = signal<ViewState>({ status: 'loading' });
  protected readonly typeLabel = vehicleTypeLabel;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.vehiclesApi.list().subscribe({
      next: (page) => this.state.set({ status: 'ready', vehicles: page.items }),
      error: (e: unknown) => this.state.set({ status: 'error', message: toApiError(e).message }),
    });
  }

  protected icon(v: Vehicle): string {
    return VEHICLE_TYPES.find((t) => t.value === v.type)?.icon ?? 'directions_car';
  }

  protected add(): void {
    this.dialog
      .open<VehicleDialog, null, Vehicle>(VehicleDialog, {
        data: null,
        width: '26rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.notify.success(`${saved.plateNumber} added.`);
        this.load();
      });
  }

  protected edit(vehicle: Vehicle): void {
    this.dialog
      .open<VehicleDialog, Vehicle, Vehicle>(VehicleDialog, {
        data: vehicle,
        width: '26rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.notify.success(`${saved.plateNumber} updated.`);
        this.load();
      });
  }

  protected remove(vehicle: Vehicle): void {
    this.confirm
      .ask({
        title: `Remove ${vehicle.plateNumber}?`,
        message:
          'This removes the vehicle from your account. A vehicle that has parked here before cannot be removed.',
        confirmLabel: 'Remove',
        destructive: true,
      })
      .subscribe((ok) => {
        if (!ok) return;
        this.vehiclesApi.delete(vehicle.id).subscribe({
          next: () => {
            this.notify.success(`${vehicle.plateNumber} removed.`);
            this.load();
          },
          error: (e: unknown) => this.notify.error(toApiError(e).message),
        });
      });
  }
}
