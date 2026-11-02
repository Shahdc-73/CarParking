import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Observable } from 'rxjs';
import { PricingApi } from '../../core/api/pricing.api';
import { toApiError } from '../../core/api/problem-details';
import { localInputToUtcIso, roundUpToMinutes, toLocalInput } from '../../core/time';
import { twoDecimals } from '../../shared/forms';
import { PricingRule } from '../../shared/models/pricing.models';
import { ErrorBanner } from '../../shared/ui/status';

export type PricingDialogData =
  { mode: 'rule'; rule: PricingRule } | { mode: 'lost-ticket'; current: number | null };

/**
 * The daily cap can never be lower than one hour's price (the API refuses it too). It is a validator on the CAP control itself,
 * because a form field only displays errors that belong to its own control.
 */
function capNotBelowRate(cap: AbstractControl): ValidationErrors | null {
  const rate = cap.parent?.get('hourlyRate')?.value as number | null | undefined;
  const value = cap.value as number | null;
  return typeof rate === 'number' && typeof value === 'number' && value < rate
    ? { capBelowRate: true }
    : null;
}

@Component({
  selector: 'app-pricing-dialog',
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
  templateUrl: './pricing-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PricingDialog {
  protected readonly data = inject<PricingDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(PricingApi);
  private readonly ref = inject<MatDialogRef<PricingDialog, boolean>>(MatDialogRef);

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly minStart = toLocalInput(new Date());

  protected readonly form = inject(NonNullableFormBuilder).group({
    hourlyRate: [
      this.data.mode === 'rule' ? this.data.rule.hourlyRate : 0,
      [Validators.required, Validators.min(0), Validators.max(1000), twoDecimals],
    ],
    dailyCap: [
      this.data.mode === 'rule' ? this.data.rule.dailyCap : 0,
      [
        Validators.required,
        Validators.min(0),
        Validators.max(100000),
        twoDecimals,
        capNotBelowRate,
      ],
    ],
    freeMinutes: [
      this.data.mode === 'rule' ? this.data.rule.freeMinutes : 15,
      [Validators.required, Validators.min(0), Validators.max(240), Validators.pattern(/^\d+$/)],
    ],
    amount: [
      this.data.mode === 'lost-ticket' ? (this.data.current ?? 0) : 0,
      [Validators.required, Validators.min(0), Validators.max(100000), twoDecimals],
    ],
    startLater: [false],
    startAt: [toLocalInput(roundUpToMinutes(new Date(Date.now() + 3_600_000), 15))],
  });

  constructor() {
    // Only the fields of the thing being edited take part in validation.
    const unused =
      this.data.mode === 'rule' ? ['amount'] : ['hourlyRate', 'dailyCap', 'freeMinutes'];
    for (const name of unused) this.form.get(name)!.disable();

    // Changing the hourly price can make an unchanged cap valid or invalid: re-check it.
    this.form.controls.hourlyRate.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.dailyCap.updateValueAndValidity());
  }

  protected get title(): string {
    return this.data.mode === 'rule'
      ? `${this.data.rule.spotType} spot pricing`
      : 'Lost-ticket fee';
  }

  protected save(): void {
    this.error.set(null);
    const v = this.form.getRawValue();
    if (this.form.invalid || (v.startLater && !v.startAt)) {
      this.form.markAllAsTouched();
      return;
    }

    // "Now" is sent as null; a later start is converted from this computer's local time to UTC.
    const effectiveFromUtc = v.startLater ? localInputToUtcIso(v.startAt) : null;
    this.saving.set(true);
    const request: Observable<unknown> =
      this.data.mode === 'rule'
        ? this.api.setRule(this.data.rule.spotType, {
            hourlyRate: v.hourlyRate,
            dailyCap: v.dailyCap,
            freeMinutes: v.freeMinutes,
            effectiveFromUtc,
          })
        : this.api.setLostTicketFee(v.amount, effectiveFromUtc);

    request.subscribe({
      next: () => this.ref.close(true),
      error: (e: unknown) => {
        this.saving.set(false);
        this.error.set(toApiError(e).message);
      },
    });
  }
}
