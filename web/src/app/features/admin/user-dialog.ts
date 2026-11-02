import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { UsersApi } from '../../core/api/admin.api';
import { toApiError } from '../../core/api/problem-details';
import { Role } from '../../core/auth/auth.models';
import { applyFieldErrors, passwordStrength } from '../../shared/forms';
import { ErrorBanner } from '../../shared/ui/status';

export const ROLE_OPTIONS: readonly { value: Role; label: string; description: string }[] = [
  {
    value: 'Attendant',
    label: 'Attendant',
    description: 'Checks vehicles in and out at the gate.',
  },
  { value: 'Admin', label: 'Admin', description: 'Manages lots, pricing and users.' },
  { value: 'Driver', label: 'Driver', description: 'Reserves spots and sees their own history.' },
];

@Component({
  selector: 'app-user-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    ErrorBanner,
  ],
  template: `
    <h2 mat-dialog-title>New user</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="form-grid">
        @if (error(); as message) {
          <app-error-banner [message]="message" [retryable]="false" />
        }
        <mat-form-field appearance="outline">
          <mat-label>Full name</mat-label>
          <input matInput formControlName="fullName" autocomplete="off" required />
          @if (form.controls.fullName.hasError('server')) {
            <mat-error>{{ form.controls.fullName.getError('server') }}</mat-error>
          } @else if (form.controls.fullName.hasError('required')) {
            <mat-error>Enter a name.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Email</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="off" required />
          @if (form.controls.email.hasError('server')) {
            <mat-error>{{ form.controls.email.getError('server') }}</mat-error>
          } @else if (form.controls.email.hasError('required')) {
            <mat-error>Enter an email address.</mat-error>
          } @else if (form.controls.email.hasError('email')) {
            <mat-error>Enter a valid email address.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Role</mat-label>
          <mat-select formControlName="role" required>
            @for (r of roles; track r.value) {
              <mat-option [value]="r.value">{{ r.label }}: {{ r.description }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Temporary password</mat-label>
          <input
            matInput
            [type]="show() ? 'text' : 'password'"
            formControlName="password"
            autocomplete="new-password"
            required
            aria-describedby="pw-rules"
          />
          <button
            mat-icon-button
            matSuffix
            type="button"
            (click)="show.set(!show())"
            [attr.aria-label]="show() ? 'Hide password' : 'Show password'"
            [attr.aria-pressed]="show()"
          >
            <mat-icon aria-hidden="true">{{ show() ? 'visibility_off' : 'visibility' }}</mat-icon>
          </button>
          <mat-hint id="pw-rules"
            >At least 8 characters with an uppercase letter, a lowercase letter and a
            digit.</mat-hint
          >
          @if (form.controls.password.hasError('server')) {
            <mat-error>{{ form.controls.password.getError('server') }}</mat-error>
          } @else if (form.controls.password.hasError('required')) {
            <mat-error>Enter a password.</mat-error>
          } @else if (form.controls.password.invalid) {
            <mat-error>The password does not meet the rules below.</mat-error>
          }
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close [disabled]="saving()">Cancel</button>
        <button mat-flat-button type="submit" [disabled]="saving()" [attr.aria-busy]="saving()">
          @if (saving()) {
            <mat-spinner diameter="20" aria-label="Creating" />
          } @else {
            Create user
          }
        </button>
      </mat-dialog-actions>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserDialog {
  private readonly api = inject(UsersApi);
  private readonly ref = inject<MatDialogRef<UserDialog, boolean>>(MatDialogRef);

  protected readonly roles = ROLE_OPTIONS;
  protected readonly show = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    fullName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    role: ['Attendant' as Role, Validators.required],
    password: ['', [Validators.required, passwordStrength, Validators.maxLength(100)]],
  });

  protected save(): void {
    this.error.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.api.create(this.form.getRawValue()).subscribe({
      next: () => this.ref.close(true),
      error: (e: unknown) => {
        this.saving.set(false);
        const apiError = toApiError(e);
        if (!applyFieldErrors(this.form, apiError.fieldErrors)) this.error.set(apiError.message);
      },
    });
  }
}
