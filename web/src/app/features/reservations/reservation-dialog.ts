import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { catchError, debounceTime, forkJoin, map, of, startWith, switchMap } from 'rxjs';
import { toApiError } from '../../core/api/problem-details';
import { ReservationsApi } from '../../core/api/reservations.api';
import { VehiclesApi } from '../../core/api/vehicles.api';
import { localInputToUtcIso, roundUpToMinutes, toLocalInput } from '../../core/time';
import { RESERVATION_DURATIONS, Reservation } from '../../shared/models/reservation.models';
import { Vehicle } from '../../shared/models/vehicle.models';
import { ErrorBanner, Loading } from '../../shared/ui/status';
import { spotTypeIcon } from '../parking/occupancy';
import { Lot, SPOT_TYPES, SpotType } from '../parking/parking.models';
import { ParkingService } from '../parking/parking.service';

type Availability =
  | { state: 'idle' }
  | { state: 'checking' }
  | { state: 'ready'; spots: number }
  | { state: 'error'; message: string };

/** The start must not be in the past (a minute of grace for the time it takes to fill in the form). */
function notInPast(control: AbstractControl): ValidationErrors | null {
  const value = control.value as string;
  if (!value) return null;
  const time = new Date(value).getTime();
  return Number.isNaN(time) || time >= Date.now() - 60_000 ? null : { past: true };
}

@Component({
  selector: 'app-reservation-dialog',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    ErrorBanner,
    Loading,
  ],
  templateUrl: './reservation-dialog.html',
  styleUrl: './reservation-dialog.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReservationDialog {
  private readonly parking = inject(ParkingService);
  private readonly vehiclesApi = inject(VehiclesApi);
  private readonly reservations = inject(ReservationsApi);
  private readonly ref = inject<MatDialogRef<ReservationDialog, Reservation>>(MatDialogRef);

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly lots = signal<Lot[]>([]);
  protected readonly vehicles = signal<Vehicle[]>([]);

  protected readonly durations = RESERVATION_DURATIONS;
  protected readonly spotTypes = SPOT_TYPES;
  protected readonly typeIcon = spotTypeIcon;
  protected readonly minStart = toLocalInput(new Date());

  protected readonly form = inject(NonNullableFormBuilder).group({
    lotId: ['', Validators.required],
    vehicleId: ['', Validators.required],
    spotType: ['Standard' as SpotType, Validators.required],
    start: [
      toLocalInput(roundUpToMinutes(new Date(Date.now() + 30 * 60_000), 15)),
      [Validators.required, notInPast],
    ],
    durationMinutes: [120, Validators.required],
  });

  protected readonly availability = signal<Availability>({ state: 'idle' });
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.loadChoices();

    // "Is a spot free then?" is checked as the person fills the form (after a short pause in typing), not only after Submit.
    this.form.valueChanges
      .pipe(
        startWith(null),
        debounceTime(300),
        switchMap(() => {
          const v = this.form.getRawValue();
          if (!v.lotId || !v.start || this.form.controls.start.invalid)
            return of<Availability>({ state: 'idle' });
          this.availability.set({ state: 'checking' });
          const { startUtc, endUtc } = this.window(v.start, v.durationMinutes);
          return this.reservations.availability(v.lotId, v.spotType, startUtc, endUtc).pipe(
            map((a): Availability => ({ state: 'ready', spots: a.availableSpots })),
            catchError((e: unknown) =>
              of<Availability>({ state: 'error', message: toApiError(e).message }),
            ),
          );
        }),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((a) => this.availability.set(a));
  }

  protected loadChoices(): void {
    this.loading.set(true);
    this.loadError.set(null);
    forkJoin({ lots: this.parking.getLots(), vehicles: this.vehiclesApi.list() }).subscribe({
      next: ({ lots, vehicles }) => {
        const open = lots.filter((l) => l.isActive);
        this.lots.set(open);
        this.vehicles.set(vehicles.items);
        // With only one choice there is nothing to decide: preselect it.
        if (open.length === 1) this.form.controls.lotId.setValue(open[0].id);
        if (vehicles.items.length === 1)
          this.form.controls.vehicleId.setValue(vehicles.items[0].id);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.loadError.set(toApiError(e).message);
        this.loading.set(false);
      },
    });
  }

  /** The form holds LOCAL wall-clock time; the API wants UTC instants. */
  private window(start: string, minutes: number): { startUtc: string; endUtc: string } {
    const startUtc = localInputToUtcIso(start);
    return { startUtc, endUtc: new Date(Date.parse(startUtc) + minutes * 60_000).toISOString() };
  }

  protected reserve(): void {
    this.error.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    this.saving.set(true);
    this.reservations
      .create({
        lotId: v.lotId,
        vehicleId: v.vehicleId,
        spotType: v.spotType,
        ...this.window(v.start, v.durationMinutes),
      })
      .subscribe({
        next: (reservation) => this.ref.close(reservation),
        error: (e: unknown) => {
          this.saving.set(false);
          this.error.set(toApiError(e).message);
        },
      });
  }
}
