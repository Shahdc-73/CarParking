import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { User } from '../core/auth/auth.models';
import { AuthService } from '../core/auth/auth.service';
import { admin, attendant, driver } from '../core/testing';
import { navItemsFor } from './nav-items';
import { Shell } from './shell';

describe('navItemsFor', () => {
  const labels = (roles: Parameters<typeof navItemsFor>[0]) =>
    navItemsFor(roles).map((i) => i.label);

  it('shows drivers only their own area and the shared lots page', () => {
    expect(labels(['Driver'])).toEqual(['My parking', 'Parking lots']);
  });
  it('shows attendants the gate', () => {
    expect(labels(['Attendant'])).toEqual(['Gate', 'Parking lots']);
  });
  it('shows admins the gate and administration', () => {
    expect(labels(['Admin'])).toEqual(['Gate', 'Administration', 'Parking lots']);
  });
  it('shows nobody an area they cannot open', () => {
    expect(labels(['Driver'])).not.toContain('Administration');
    expect(labels(['Driver'])).not.toContain('Gate');
  });
});

describe('Shell', () => {
  const current = signal<User | null>(driver);
  let logout: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Shell>;
  let el: HTMLElement;

  beforeEach(async () => {
    logout = vi.fn().mockReturnValue(of(undefined));
    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { user: current.asReadonly(), logout } },
      ],
    }).compileComponents();
  });

  const render = () => {
    fixture = TestBed.createComponent(Shell);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  it('renders navigation for the signed-in role inside a labelled landmark', () => {
    current.set(attendant);
    render();
    const nav = el.querySelector('nav[aria-label="Main"]')!;
    expect(
      [...nav.querySelectorAll('a')].map((a) => a.textContent!.replace(/\s+/g, ' ').trim()),
    ).toEqual([expect.stringContaining('Gate'), expect.stringContaining('Parking lots')]);
  });

  it('shows an admin the admin link', () => {
    current.set(admin);
    render();
    expect(el.textContent).toContain('Administration');
  });

  it('offers a skip link and a focusable main landmark', () => {
    current.set(driver);
    render();
    expect(el.querySelector('a.skip-link')?.getAttribute('href')).toBe('#main');
    expect(el.querySelector('main#main')?.getAttribute('tabindex')).toBe('-1');
  });

  it('shows the user name in the toolbar', () => {
    current.set(driver);
    render();
    expect(el.querySelector('.user-button')?.textContent).toContain('Dana Driver');
  });

  it('signing out calls the service and goes to the login page', () => {
    current.set(driver);
    render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    (fixture.componentInstance as unknown as { logout(): void }).logout();

    expect(logout).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('shows the avatar initials and the role next to the name', () => {
    current.set(attendant);
    render();
    const button = el.querySelector('.user-button')!;
    expect(button.querySelector('.avatar')?.textContent).toBe('SA');
    expect(button.querySelector('.user-role')?.textContent).toBe('Attendant');
    expect(button.querySelector('.avatar')?.getAttribute('aria-hidden')).toBe('true'); // decorative; the name is already read
  });

  it('marks the current page in the navigation for assistive technology', async () => {
    current.set(driver);
    TestBed.inject(Router).resetConfig([
      { path: 'lots', component: Shell },
      { path: '**', component: Shell },
    ]);
    render();
    await TestBed.inject(Router).navigateByUrl('/lots');
    fixture.detectChanges();

    const current$ = el.querySelector('nav a[aria-current="page"]');
    expect(current$?.textContent).toContain('Parking lots');
  });
});
