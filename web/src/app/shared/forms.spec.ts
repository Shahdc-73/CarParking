import { FormControl, FormGroup } from '@angular/forms';
import {
  DirtyOrSubmittedErrorMatcher,
  applyFieldErrors,
  passwordStrength,
  plateNumber,
  twoDecimals,
} from './forms';

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

describe('plateNumber', () => {
  const check = (value: string) => plateNumber(new FormControl(value));

  it.each(['AB', 'ab-123 c', ' 12 34 ', 'ABCDE12345'])('accepts %j', (v) =>
    expect(check(v)).toBeNull(),
  );
  it.each(['A', '!!', 'ABCDEFGHIJK', 'AB_12', 'AB.12'])('rejects %j', (v) =>
    expect(check(v)).toEqual({ plate: true }),
  );
  it('leaves an empty value to `required`', () => expect(check('')).toBeNull());
});

describe('twoDecimals', () => {
  const check = (value: number | null) => twoDecimals(new FormControl(value));

  it.each([0, 3, 3.5, 3.05, 0.1 + 0.2 - 0.3 + 1, 19.99, 100000])('accepts %s', (v) =>
    expect(check(v)).toBeNull(),
  );
  it.each([3.005, 0.001, 1.999])('rejects %s', (v) => expect(check(v)).toEqual({ decimals: true }));
  it('ignores an empty value', () => {
    expect(check(null)).toBeNull();
    expect(check(Number.NaN)).toBeNull();
  });
});

describe('DirtyOrSubmittedErrorMatcher', () => {
  const matcher = new DirtyOrSubmittedErrorMatcher();
  const invalid = () => new FormControl('', { validators: () => ({ bad: true }) });

  it('does not nag about an untouched or merely visited field', () => {
    const control = invalid();
    control.markAsTouched();
    expect(matcher.isErrorState(control, null)).toBe(false);
  });

  it('shows the error once the user has typed something invalid', () => {
    const control = invalid();
    control.markAsDirty();
    expect(matcher.isErrorState(control, null)).toBe(true);
  });

  it('shows the error after a submit attempt', () => {
    expect(matcher.isErrorState(invalid(), { submitted: true } as never)).toBe(true);
  });

  it('never flags a valid field', () => {
    const ok = new FormControl('x');
    ok.markAsDirty();
    expect(matcher.isErrorState(ok, { submitted: true } as never)).toBe(false);
  });
});
