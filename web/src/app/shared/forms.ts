import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';

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
