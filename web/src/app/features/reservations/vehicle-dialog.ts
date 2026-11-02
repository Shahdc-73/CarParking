import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { toApiError } from '../../core/api/problem-details';
import { VehiclesApi } from '../../core/api/vehicles.api';
import { applyFieldErrors, plateNumber } from '../../shared/forms';
import { VEHICLE_TYPES, Vehicle } from '../../shared/models/vehicle.models';
import { ErrorBanner } from '../../shared/ui/status';

/** Add a vehicle, or edit type/colour of an existing one (the plate is its identity and cannot change). */
@Component({
  selector: 'app-vehicle-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    ErrorBanner,
  ],
  template: `
    <h2 mat-dialog-title>{{ editing ? 'Edit vehicle' : 'Add a vehicle' }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="form-grid">
        @if (error(); as message) {
          <app-error-banner [message]="message" [retryable]="false" />
        }

        <mat-form-field appearance="outline">
          <mat-label>Plate number</mat-label>
          <input
            matInput
            formControlName="plateNumber"
            autocomplete="off"
            autocapitalize="characters"
            spellcheck="false"
            required
            class="plate"
          />
          @if (editing) {
            <mat-hint
              >The plate cannot be changed. Delete the vehicle and add it again if it was typed
              wrong.</mat-hint
            >
          }
          @if (form.controls.plateNumber.hasError('server')) {
            <mat-error>{{ form.controls.plateNumber.getError('server') }}</mat-error>
          } @else if (form.controls.plateNumber.hasError('required')) {
            <mat-error>Enter the plate number.</mat-error>
          } @else if (form.controls.plateNumber.hasError('plate')) {
            <mat-error>Use 2 to 10 letters or digits (spaces and dashes are ignored).</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Vehicle type</mat-label>
          <mat-select formControlName="type" required>
            @for (t of types; track t.value) {
              <mat-option [value]="t.value">{{ t.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Colour</mat-label>
          <input matInput formControlName="color" autocomplete="off" maxlength="30" required />
          @if (form.controls.color.hasError('server')) {
            <mat-error>{{ form.controls.color.getError('server') }}</mat-error>
          } @else if (form.controls.color.hasError('required')) {
            <mat-error>Enter the colour.</mat-error>
          }
        </mat-form-field>
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close [disabled]="saving()">Cancel</button>
        <button mat-flat-button type="submit" [disabled]="saving()" [attr.aria-busy]="saving()">
          @if (saving()) {
            <mat-spinner diameter="20" aria-label="Saving" />
          } @else {
            {{ editing ? 'Save changes' : 'Add vehicle' }}
          }
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .plate {
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VehicleDialog {
  private readonly vehicles = inject(VehiclesApi);
  private readonly ref = inject<MatDialogRef<VehicleDialog, Vehicle>>(MatDialogRef);
  protected readonly existing = inject<Vehicle | null>(MAT_DIALOG_DATA, { optional: true });

  protected readonly editing = this.existing !== null;
  protected readonly types = VEHICLE_TYPES;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    plateNumber: [
      { value: this.existing?.plateNumber ?? '', disabled: this.existing !== null },
      [Validators.required, plateNumber],
    ],
    type: [this.existing?.type ?? ('Car' as Vehicle['type']), Validators.required],
    color: [this.existing?.color ?? '', [Validators.required, Validators.maxLength(30)]],
  });

  protected save(): void {
    this.error.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { plateNumber: plate, type, color } = this.form.getRawValue();
    this.saving.set(true);
    const request = this.existing
      ? this.vehicles.update(this.existing.id, { type, color })
      : this.vehicles.create({ plateNumber: plate, type, color });
    request.subscribe({
      next: (vehicle) => this.ref.close(vehicle),
      error: (e: unknown) => {
        this.saving.set(false);
        const apiError = toApiError(e);
        if (!applyFieldErrors(this.form, apiError.fieldErrors)) this.error.set(apiError.message);
      },
    });
  }
}
