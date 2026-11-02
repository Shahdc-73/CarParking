import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { toApiError } from '../../core/api/problem-details';
import { LotsAdminApi } from '../../core/api/admin.api';
import { applyFieldErrors } from '../../shared/forms';
import { ErrorBanner } from '../../shared/ui/status';
import { SPOT_TYPES, SpotType } from '../parking/parking.models';

export type SpotDialogData =
  | { mode: 'add'; levelId: string; levelName: string }
  | { mode: 'edit'; spotId: string; code: string; type: SpotType };

@Component({
  selector: 'app-spot-dialog',
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
    <h2 mat-dialog-title>
      {{ data.mode === 'add' ? 'Add a spot to ' + data.levelName : 'Edit spot ' + data.code }}
    </h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="form-grid">
        @if (error(); as message) {
          <app-error-banner [message]="message" [retryable]="false" />
        }
        <mat-form-field appearance="outline">
          <mat-label>Spot code</mat-label>
          <input
            matInput
            formControlName="code"
            maxlength="20"
            autocomplete="off"
            required
            class="code"
          />
          <mat-hint>Letters, digits and dashes, unique on its level. Example: A-12</mat-hint>
          @if (form.controls.code.hasError('server')) {
            <mat-error>{{ form.controls.code.getError('server') }}</mat-error>
          } @else if (form.controls.code.hasError('required')) {
            <mat-error>Enter a spot code.</mat-error>
          } @else if (form.controls.code.hasError('pattern')) {
            <mat-error>Use only letters, digits and dashes (max 20).</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Spot type</mat-label>
          <mat-select formControlName="type" required>
            @for (t of types; track t) {
              <mat-option [value]="t">{{ t }}</mat-option>
            }
          </mat-select>
          @if (data.mode === 'edit') {
            <mat-hint
              >The type cannot change while the spot is in use or has pending
              reservations.</mat-hint
            >
          }
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close [disabled]="saving()">Cancel</button>
        <button mat-flat-button type="submit" [disabled]="saving()" [attr.aria-busy]="saving()">
          @if (saving()) {
            <mat-spinner diameter="20" aria-label="Saving" />
          } @else {
            {{ data.mode === 'add' ? 'Add spot' : 'Save changes' }}
          }
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .code {
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SpotDialog {
  protected readonly data = inject<SpotDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(LotsAdminApi);
  private readonly ref = inject<MatDialogRef<SpotDialog, boolean>>(MatDialogRef);

  protected readonly types = SPOT_TYPES;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    code: [
      this.data.mode === 'edit' ? this.data.code : '',
      [Validators.required, Validators.pattern(/^[A-Za-z0-9-]{1,20}$/)],
    ],
    type: [
      this.data.mode === 'edit' ? this.data.type : ('Standard' as SpotType),
      Validators.required,
    ],
  });

  protected save(): void {
    this.error.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    const value = this.form.getRawValue();
    const request =
      this.data.mode === 'add'
        ? this.api.addSpot(this.data.levelId, value)
        : this.api.updateSpot(this.data.spotId, value);
    request.subscribe({
      next: () => this.ref.close(true),
      error: (e: unknown) => {
        this.saving.set(false);
        const apiError = toApiError(e);
        if (!applyFieldErrors(this.form, apiError.fieldErrors)) this.error.set(apiError.message);
      },
    });
  }
}
