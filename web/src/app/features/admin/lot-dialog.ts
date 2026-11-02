import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { toApiError } from '../../core/api/problem-details';
import { LotsAdminApi } from '../../core/api/admin.api';
import { applyFieldErrors } from '../../shared/forms';
import { ErrorBanner } from '../../shared/ui/status';

export interface LotDialogData {
  id: string;
  name: string;
  address: string;
  allowMotorcycleOverflow: boolean;
}

/** Create a lot (data = null) or edit one. */
@Component({
  selector: 'app-lot-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    ErrorBanner,
  ],
  template: `
    <h2 mat-dialog-title>{{ existing ? 'Edit lot' : 'New lot' }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="form-grid">
        @if (error(); as message) {
          <app-error-banner [message]="message" [retryable]="false" />
        }
        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" maxlength="100" required />
          @if (form.controls.name.hasError('server')) {
            <mat-error>{{ form.controls.name.getError('server') }}</mat-error>
          } @else if (form.controls.name.hasError('required')) {
            <mat-error>Enter a name.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Address</mat-label>
          <input matInput formControlName="address" maxlength="300" required />
          @if (form.controls.address.hasError('server')) {
            <mat-error>{{ form.controls.address.getError('server') }}</mat-error>
          } @else if (form.controls.address.hasError('required')) {
            <mat-error>Enter an address.</mat-error>
          }
        </mat-form-field>
        <mat-checkbox formControlName="allowMotorcycleOverflow">
          Motorcycles may use car spots when no motorcycle spot is free
        </mat-checkbox>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close [disabled]="saving()">Cancel</button>
        <button mat-flat-button type="submit" [disabled]="saving()" [attr.aria-busy]="saving()">
          @if (saving()) {
            <mat-spinner diameter="20" aria-label="Saving" />
          } @else {
            {{ existing ? 'Save changes' : 'Create lot' }}
          }
        </button>
      </mat-dialog-actions>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LotDialog {
  private readonly api = inject(LotsAdminApi);
  private readonly ref = inject<MatDialogRef<LotDialog, boolean>>(MatDialogRef);
  protected readonly existing = inject<LotDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.existing?.name ?? '', [Validators.required, Validators.maxLength(100)]],
    address: [this.existing?.address ?? '', [Validators.required, Validators.maxLength(300)]],
    allowMotorcycleOverflow: [this.existing?.allowMotorcycleOverflow ?? false],
  });

  protected save(): void {
    this.error.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    const value = this.form.getRawValue();
    const request = this.existing
      ? this.api.update(this.existing.id, value)
      : this.api.create(value);
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
