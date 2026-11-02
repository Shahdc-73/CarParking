import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../core/auth/auth.service';
import { User } from '../../core/auth/auth.models';
import { admin, attendant, driver } from '../../core/testing';
import { Login } from './login';

describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let el: HTMLElement;
  let login: ReturnType<typeof vi.fn<(r: { email: string; password: string }) => Observable<User>>>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    login = vi.fn();
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideRouter([]), { provide: AuthService, useValue: { login } }],
    }).compileComponents();

    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(Login);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  const type = (selector: string, value: string) => {
    const input = el.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const submit = async () => {
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const fill = () => {
    type('input[formControlName=email]', 'dana@parkwise.local');
    type('input[formControlName=password]', 'secret');
  };

  it('has labelled fields and the right autocomplete hints for password managers', () => {
    expect(el.querySelector('input[type=email]')!.getAttribute('autocomplete')).toBe('username');
    expect(el.querySelector('input[type=password]')!.getAttribute('autocomplete')).toBe(
      'current-password',
    );
    expect(el.textContent).toContain('Email');
    expect(el.textContent).toContain('Password');
  });

  it('shows what is missing and does not call the server when the form is empty', async () => {
    await submit();

    expect(login).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Enter your email address.');
    expect(el.textContent).toContain('Enter your password.');
  });

  it('rejects a malformed email before calling the server', async () => {
    type('input[formControlName=email]', 'not-an-email');
    type('input[formControlName=password]', 'secret');
    await submit();

    expect(login).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Enter a valid email address.');
  });

  it.each([
    [driver, '/my'],
    [attendant, '/gate'],
    [admin, '/admin'],
  ])('sends %j to their own home after signing in', async (user, home) => {
    login.mockReturnValue(of(user));
    fill();
    await submit();

    expect(login).toHaveBeenCalledWith({ email: 'dana@parkwise.local', password: 'secret' });
    expect(navigateByUrl).toHaveBeenCalledWith(home);
  });

  it('returns to the page the user was trying to open', async () => {
    login.mockReturnValue(of(driver));
    fixture.componentRef.setInput('returnUrl', '/lots');
    fill();
    await submit();

    expect(navigateByUrl).toHaveBeenCalledWith('/lots');
  });

  it.each(['https://evil.example', '//evil.example', 'javascript:alert(1)'])(
    'ignores a hostile returnUrl (%s) and goes home instead',
    async (hostile) => {
      login.mockReturnValue(of(driver));
      fixture.componentRef.setInput('returnUrl', hostile);
      fill();
      await submit();

      expect(navigateByUrl).toHaveBeenCalledWith('/my');
    },
  );

  it('shows the server message in an alert and lets the user try again', async () => {
    login.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 401, error: { title: 'Invalid email or password.' } }),
      ),
    );
    fill();
    await submit();

    const alert = el.querySelector('[role=alert]');
    expect(alert?.textContent).toContain('Invalid email or password.');
    expect(navigateByUrl).not.toHaveBeenCalled();
    expect(el.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
  });

  it('disables the button and announces progress while signing in, so it cannot be double-submitted', async () => {
    const pending = new Subject<User>();
    login.mockReturnValue(pending);
    fill();
    await submit();

    const button = el.querySelector<HTMLButtonElement>('button[type=submit]')!;
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(login).toHaveBeenCalledTimes(1);
  });

  it('links to registration', () => {
    expect(el.querySelector('a[href="/register"]')).not.toBeNull();
  });

  it('lets the user reveal and hide the password, announcing the state to screen readers', async () => {
    const input = el.querySelector<HTMLInputElement>('input[formControlName=password]')!;
    const toggle = el.querySelector<HTMLButtonElement>('button[aria-pressed]')!;
    expect(input.type).toBe('password');
    expect(toggle.getAttribute('aria-label')).toBe('Show password');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    expect(toggle.type).toBe('button'); // must never submit the form

    toggle.click();
    fixture.detectChanges();
    expect(input.type).toBe('text');
    expect(toggle.getAttribute('aria-label')).toBe('Hide password');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');

    toggle.click();
    fixture.detectChanges();
    expect(input.type).toBe('password');
  });
});
