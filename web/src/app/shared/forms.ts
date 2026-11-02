import {
  AbstractControl,
  FormGroup,
  FormGroupDirective,
  NgForm,
  ValidationErrors,
  ValidatorFn,
} from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';

/**
 * Mirrors the backend password rules (8+ chars, upper, lower, digit) so people see the problem while typing instead of after
 * a round trip. The server still validates: this is convenience, never trust.
 */
export const passwordStrength: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const value = (control.value as string) ?? '';
  if (!value) return null; // `required` reports an empty value
  const errors: ValidationErrors = {};
  if (value.length < 8) errors['minlength'] = true;
  if (!/[A-Z]/.test(value)) errors['uppercase'] = true;
  if (!/[a-z]/.test(value)) errors['lowercase'] = true;
  if (!/[0-9]/.test(value)) errors['digit'] = true;
  return Object.keys(errors).length ? errors : null;
};

/** Shows the API's per-field validation messages on the matching form controls (keys are camelCase, like the controls). */
export function applyFieldErrors(form: FormGroup, fieldErrors: Record<string, string[]>): boolean {
  let applied = false;
  for (const [field, messages] of Object.entries(fieldErrors)) {
    const control = form.get(field);
    if (control && messages.length) {
      control.setErrors({ server: messages[0] });
      control.markAsTouched();
      applied = true;
    }
  }
  return applied;
}

/** Plate numbers: 2-10 letters/digits once spaces and dashes are ignored (same rule as the API). */
export const plateNumber: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = ((control.value as string) ?? '').trim();
  if (!value) return null; // `required` reports an empty value
  const stripped = value.replace(/[\s-]/g, '');
  return /^[A-Za-z0-9]{2,10}$/.test(stripped) ? null : { plate: true };
};

/**
 * Material's default shows an error as soon as a required field is merely visited and left empty. For a field that is
 * deliberately left empty between tasks (the gate's plate box) that is noise. Show errors only once the user typed something
 * or tried to submit.
 */
export class DirtyOrSubmittedErrorMatcher implements ErrorStateMatcher {
  isErrorState(control: AbstractControl | null, form: FormGroupDirective | NgForm | null): boolean {
    return !!(control && control.invalid && (control.dirty || form?.submitted));
  }
}

/** At most 2 decimal places (money). Works around 0.1 + 0.2 style float noise by comparing in cents. */
export const twoDecimals: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = control.value as number | null;
  if (value === null || value === undefined || Number.isNaN(value)) return null;
  return Math.abs(value * 100 - Math.round(value * 100)) < 1e-6 ? null : { decimals: true };
};
