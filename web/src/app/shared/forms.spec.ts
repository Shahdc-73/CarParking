import { FormControl, FormGroup } from '@angular/forms';
import { applyFieldErrors, passwordStrength } from './forms';

describe('passwordStrength', () => {
  const check = (value: string) => passwordStrength(new FormControl(value));

  it('accepts a password meeting every rule', () => expect(check('Passw0rd1')).toBeNull());
  it('leaves an empty value to the `required` validator', () => expect(check('')).toBeNull());

  it.each([
    ['Ab1', { minlength: true }],
    ['password1', { uppercase: true }],
    ['PASSWORD1', { lowercase: true }],
    ['Passwordd', { digit: true }],
  ])('reports exactly what is missing from %s', (value, expected) =>
    expect(check(value)).toEqual(expected),
  );

  it('reports every missing rule at once', () => {
    expect(check('abc')).toEqual({ minlength: true, uppercase: true, digit: true });
  });
});

describe('applyFieldErrors', () => {
  const makeForm = () =>
    new FormGroup({ email: new FormControl(''), password: new FormControl('') });

  it('puts the first server message on the matching control and marks it touched', () => {
    const form = makeForm();
    expect(applyFieldErrors(form, { email: ['Bad email', 'Other'] })).toBe(true);
    expect(form.controls.email.getError('server')).toBe('Bad email');
    expect(form.controls.email.touched).toBe(true);
    expect(form.controls.password.errors).toBeNull();
  });

  it('ignores fields the form does not have and reports that nothing was applied', () => {
    const form = makeForm();
    expect(applyFieldErrors(form, { unknown: ['x'] })).toBe(false);
    expect(applyFieldErrors(form, {})).toBe(false);
  });
});
