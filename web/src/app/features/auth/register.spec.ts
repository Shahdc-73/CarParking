import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { User } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { driver } from '../../core/testing';
import { Register } from './register';

describe('Register', () => {
  let fixture: ComponentFixture<Register>;
  let el: HTMLElement;
  let register: ReturnType<
    typeof vi.fn<(r: { email: string; password: string; fullName: string }) => Observable<User>>
  >;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    register = vi.fn();
    await TestBed.configureTestingModule({
      imports: [Register],
      providers: [provideRouter([]), { provide: AuthService, useValue: { register } }],
    }).compileComponents();
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(Register);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  const type = (name: string, value: string) => {
    const input = el.querySelector<HTMLInputElement>(`input[formControlName=${name}]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };
  const fill = (password = 'Passw0rd1') => {
    type('fullName', 'Dana Driver');
    type('email', 'dana@parkwise.local');
    type('password', password);
  };
  const submit = async () => {
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  it('uses new-password autocomplete and explains the password rules up front', () => {
    expect(el.querySelector('input[type=password]')!.getAttribute('autocomplete')).toBe(
      'new-password',
    );
    expect(el.querySelector('#password-rules')?.textContent).toMatch(
      /8 characters.*uppercase.*lowercase.*digit/i,
    );
  });

  it('does not call the server when required fields are empty', async () => {
    await submit();
    expect(register).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Enter your name.');
    expect(el.textContent).toContain('Choose a password.');
  });

  it('tells the user exactly which password rules are not met', async () => {
    fill('abc');
    await submit();

    expect(register).not.toHaveBeenCalled();
    const text = el.textContent!;
    expect(text).toContain('8+ characters');
    expect(text).toContain('An uppercase letter');
    expect(text).toContain('A digit');
    expect(text).not.toContain('A lowercase letter');
  });

  it('creates the account and lands on the driver home', async () => {
    register.mockReturnValue(of(driver));
    fill();
    await submit();

    expect(register).toHaveBeenCalledWith({
      fullName: 'Dana Driver',
      email: 'dana@parkwise.local',
      password: 'Passw0rd1',
    });
    expect(navigateByUrl).toHaveBeenCalledWith('/my');
  });

  it("shows the server's per-field validation errors next to the fields", async () => {
    register.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { title: 'Validation', errors: { email: ['The email is not allowed.'] } },
          }),
      ),
    );
    fill();
    await submit();

    expect(el.textContent).toContain('The email is not allowed.');
    expect(el.querySelector('[role=alert]')).toBeNull(); // handled inline, no duplicate banner
  });

  it('shows a general failure (e.g. email already registered) in the alert banner', async () => {
    register.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { title: 'An account with this email already exists.' },
          }),
      ),
    );
    fill();
    await submit();

    expect(el.querySelector('[role=alert]')?.textContent).toContain(
      'An account with this email already exists.',
    );
    expect(navigateByUrl).not.toHaveBeenCalled();
  });
});
