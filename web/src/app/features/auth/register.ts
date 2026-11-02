import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { toApiError } from '../../core/api/problem-details';
import { homeFor } from '../../core/auth/auth.guards';
import { AuthService } from '../../core/auth/auth.service';
import { applyFieldErrors, passwordStrength } from '../../shared/forms';
import { ErrorBanner } from '../../shared/ui/status';
import { AuthLayout } from './auth-layout';

@Component({
  selector: 'app-register',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    ErrorBanner,
    AuthLayout,
  ],
  templateUrl: './register.html',
  styleUrl: './auth-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(NonNullableFormBuilder).group({
    fullName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', [Validators.required, passwordStrength, Validators.maxLength(100)]],
  });
  protected readonly showPassword = signal(false);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    this.error.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.auth.register(this.form.getRawValue()).subscribe({
      next: (user) => void this.router.navigateByUrl(homeFor(user.roles)),
      error: (e: unknown) => {
        this.submitting.set(false);
        const apiError = toApiError(e);
        // Field-level problems go next to the field; anything else (e.g. "email already registered") goes in the banner.
        if (!applyFieldErrors(this.form, apiError.fieldErrors)) this.error.set(apiError.message);
      },
    });
  }
}
