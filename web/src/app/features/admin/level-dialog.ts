import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { toApiError } from '../../core/api/problem-details';
import { LotsAdminApi } from '../../core/api/admin.api';
import { applyFieldErrors } from '../../shared/forms';
import { LotLevel } from '../../shared/models/admin.models';
import { ErrorBanner } from '../../shared/ui/status';

export type LevelDialogData =
  { mode: 'add'; lotId: string; nextNumber: number } | { mode: 'rename'; level: LotLevel };

@Component({
  selector: 'app-level-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    ErrorBanner,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.mode === 'add' ? 'Add a level' : 'Rename level' }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="form-grid">
        @if (error(); as message) {
          <app-error-banner [message]="message" [retryable]="false" />
        }
        <mat-form-field appearance="outline">
          <mat-label>Level number</mat-label>
          <input matInput type="number" formControlName="number" min="-10" max="200" required />
          <mat-hint
            >Use negative numbers for basements. A level's number cannot be changed later.</mat-hint
          >
          @if (form.controls.number.hasError('server')) {
            <mat-error>{{ form.controls.number.getError('server') }}</mat-error>
          } @else if (form.controls.number.invalid) {
            <mat-error>Enter a whole number from -10 to 200.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" maxlength="50" required />
          @if (form.controls.name.hasError('server')) {
            <mat-error>{{ form.controls.name.getError('server') }}</mat-error>
          } @else if (form.controls.name.hasError('required')) {
            <mat-error>Enter a name, for example "Ground" or "Roof".</mat-error>
          }
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close [disabled]="saving()">Cancel</button>
        <button mat-flat-button type="submit" [disabled]="saving()" [attr.aria-busy]="saving()">
          @if (saving()) {
            <mat-spinner diameter="20" aria-label="Saving" />
          } @else {
            {{ data.mode === 'add' ? 'Add level' : 'Save' }}
          }
        </button>
      </mat-dialog-actions>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LevelDialog {
  protected readonly data = inject<LevelDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(LotsAdminApi);
  private readonly ref = inject<MatDialogRef<LevelDialog, boolean>>(MatDialogRef);

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    number: [
      {
        value: this.data.mode === 'add' ? this.data.nextNumber : this.data.level.number,
        disabled: this.data.mode === 'rename',
      },
      [
        Validators.required,
        Validators.min(-10),
        Validators.max(200),
        Validators.pattern(/^-?\d+$/),
      ],
    ],
    name: [
      this.data.mode === 'rename' ? this.data.level.name : '',
      [Validators.required, Validators.maxLength(50)],
    ],
  });

  protected save(): void {
    this.error.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { number, name } = this.form.getRawValue();
    this.saving.set(true);
    const request =
      this.data.mode === 'add'
        ? this.api.addLevel(this.data.lotId, Number(number), name)
        : this.api.renameLevel(this.data.level.id, name);
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
