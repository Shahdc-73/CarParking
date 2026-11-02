import { HttpErrorResponse } from '@angular/common/http';
import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { LotsAdminApi, UsersApi } from '../../core/api/admin.api';
import { PricingApi } from '../../core/api/pricing.api';
import { LevelDialog } from './level-dialog';
import { LotDialog } from './lot-dialog';
import { PricingDialog } from './pricing-dialog';
import { SpotDialog } from './spot-dialog';
import { UserDialog } from './user-dialog';

/** Creates a dialog with a stub API and a recording MatDialogRef: the only things a dialog talks to. */
async function open<T>(component: Type<T>, data: unknown, apis: unknown[]) {
  const close = vi.fn();
  await TestBed.configureTestingModule({
    imports: [component],
    providers: [
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: MatDialogRef, useValue: { close } },
      ...(apis as never[]),
    ],
  }).compileComponents();
  const fixture = TestBed.createComponent(component) as ComponentFixture<T>;
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const fill = (name: string, value: string) => {
    const input = el.querySelector<HTMLInputElement>(`input[formControlName=${name}]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };
  const submit = async () => {
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  return {
    fixture,
    el,
    close,
    fill,
    submit,
    form: () => (fixture.componentInstance as { form: import('@angular/forms').FormGroup }).form,
  };
}

const problem = (status: number, title: string, errors?: Record<string, string[]>) =>
  new HttpErrorResponse({ status, error: { title, errors } });

describe('LotDialog', () => {
  it('creates a lot and closes with true', async () => {
    const create = vi.fn().mockReturnValue(of({}));
    const d = await open(LotDialog, null, [{ provide: LotsAdminApi, useValue: { create } }]);
    expect(d.el.querySelector('h2')?.textContent).toContain('New lot');
    d.fill('name', 'Harbour');
    d.fill('address', '1 Quay');
    await d.submit();

    expect(create).toHaveBeenCalledWith({
      name: 'Harbour',
      address: '1 Quay',
      allowMotorcycleOverflow: false,
    });
    expect(d.close).toHaveBeenCalledWith(true);
  });

  it('requires a name and an address', async () => {
    const create = vi.fn();
    const d = await open(LotDialog, null, [{ provide: LotsAdminApi, useValue: { create } }]);
    await d.submit();
    expect(create).not.toHaveBeenCalled();
    expect(d.el.textContent).toContain('Enter a name.');
    expect(d.el.textContent).toContain('Enter an address.');
  });

  it('edits an existing lot with its current values', async () => {
    const update = vi.fn().mockReturnValue(of({}));
    const d = await open(
      LotDialog,
      { id: 'l1', name: 'Old', address: 'Addr', allowMotorcycleOverflow: true },
      [{ provide: LotsAdminApi, useValue: { update } }],
    );
    expect(d.el.querySelector<HTMLInputElement>('input[formControlName=name]')!.value).toBe('Old');
    d.fill('name', 'New');
    await d.submit();
    expect(update).toHaveBeenCalledWith('l1', {
      name: 'New',
      address: 'Addr',
      allowMotorcycleOverflow: true,
    });
  });

  it('shows "name already taken" on the name field', async () => {
    const create = vi
      .fn()
      .mockReturnValue(
        throwError(() =>
          problem(400, 'Invalid', { name: ['A lot with this name already exists.'] }),
        ),
      );
    const d = await open(LotDialog, null, [{ provide: LotsAdminApi, useValue: { create } }]);
    d.fill('name', 'Harbour');
    d.fill('address', 'x');
    await d.submit();
    expect(d.el.textContent).toContain('A lot with this name already exists.');
    expect(d.close).not.toHaveBeenCalled();
  });
});

describe('LevelDialog', () => {
  it('adds a level, suggesting the next number', async () => {
    const addLevel = vi.fn().mockReturnValue(of({}));
    const d = await open(LevelDialog, { mode: 'add', lotId: 'l1', nextNumber: 4 }, [
      { provide: LotsAdminApi, useValue: { addLevel } },
    ]);
    expect(d.el.querySelector<HTMLInputElement>('input[formControlName=number]')!.value).toBe('4');
    d.fill('name', 'Roof');
    await d.submit();
    expect(addLevel).toHaveBeenCalledWith('l1', 4, 'Roof');
  });

  it('rejects numbers outside -10..200 and non-integers', async () => {
    const addLevel = vi.fn();
    const d = await open(LevelDialog, { mode: 'add', lotId: 'l1', nextNumber: 1 }, [
      { provide: LotsAdminApi, useValue: { addLevel } },
    ]);
    d.fill('name', 'X');
    for (const bad of ['201', '-11', '1.5']) {
      d.fill('number', bad);
      await d.submit();
    }
    expect(addLevel).not.toHaveBeenCalled();
  });

  it("renaming locks the number (a level's number is its identity)", async () => {
    const renameLevel = vi.fn().mockReturnValue(of({}));
    const d = await open(
      LevelDialog,
      { mode: 'rename', level: { id: 'lv1', lotId: 'l1', number: 2, name: 'Old', spotCount: 5 } },
      [{ provide: LotsAdminApi, useValue: { renameLevel } }],
    );
    expect(d.el.querySelector<HTMLInputElement>('input[formControlName=number]')!.disabled).toBe(
      true,
    );
    d.fill('name', 'Terrace');
    await d.submit();
    expect(renameLevel).toHaveBeenCalledWith('lv1', 'Terrace');
  });

  it('shows "level already exists" as a banner when the server refuses', async () => {
    const addLevel = vi
      .fn()
      .mockReturnValue(throwError(() => problem(409, 'Level 2 already exists in this lot.')));
    const d = await open(LevelDialog, { mode: 'add', lotId: 'l1', nextNumber: 2 }, [
      { provide: LotsAdminApi, useValue: { addLevel } },
    ]);
    d.fill('name', 'Dup');
    await d.submit();
    expect(d.el.querySelector('[role=alert]')?.textContent).toContain('already exists');
  });
});

describe('SpotDialog', () => {
  it('adds a spot to a level', async () => {
    const addSpot = vi.fn().mockReturnValue(of({}));
    const d = await open(SpotDialog, { mode: 'add', levelId: 'lv1', levelName: 'Ground' }, [
      { provide: LotsAdminApi, useValue: { addSpot } },
    ]);
    expect(d.el.querySelector('h2')?.textContent).toContain('Add a spot to Ground');
    d.fill('code', 'a-12');
    await d.submit();
    expect(addSpot).toHaveBeenCalledWith('lv1', { code: 'a-12', type: 'Standard' });
  });

  it('only accepts letters, digits and dashes', async () => {
    const addSpot = vi.fn();
    const d = await open(SpotDialog, { mode: 'add', levelId: 'lv1', levelName: 'Ground' }, [
      { provide: LotsAdminApi, useValue: { addSpot } },
    ]);
    d.fill('code', 'bad code!');
    await d.submit();
    expect(addSpot).not.toHaveBeenCalled();
    expect(d.el.textContent).toContain('Use only letters, digits and dashes');
  });

  it('edits code and type of an existing spot', async () => {
    const updateSpot = vi.fn().mockReturnValue(of({}));
    const d = await open(
      SpotDialog,
      { mode: 'edit', spotId: 's1', code: 'A-01', type: 'Standard' },
      [{ provide: LotsAdminApi, useValue: { updateSpot } }],
    );
    d.form().controls['type'].setValue('EV');
    await d.submit();
    expect(updateSpot).toHaveBeenCalledWith('s1', { code: 'A-01', type: 'EV' });
  });

  it('shows why a type change was refused (spot in use)', async () => {
    const updateSpot = vi
      .fn()
      .mockReturnValue(
        throwError(() => problem(409, 'Spot A-01 is occupied; its type cannot change.')),
      );
    const d = await open(
      SpotDialog,
      { mode: 'edit', spotId: 's1', code: 'A-01', type: 'Standard' },
      [{ provide: LotsAdminApi, useValue: { updateSpot } }],
    );
    await d.submit();
    expect(d.el.querySelector('[role=alert]')?.textContent).toContain('cannot change');
  });
});

describe('PricingDialog', () => {
  const rule = {
    spotType: 'Standard' as const,
    hourlyRate: 3,
    dailyCap: 20,
    freeMinutes: 15,
    effectiveFromUtc: '2025-01-01T00:00:00Z',
  };

  it('shows the current values and saves a new version effective now', async () => {
    const setRule = vi.fn().mockReturnValue(of({}));
    const d = await open(PricingDialog, { mode: 'rule', rule }, [
      { provide: PricingApi, useValue: { setRule } },
    ]);
    expect(d.el.querySelector('h2')?.textContent).toContain('Standard spot pricing');
    expect(d.el.textContent).toContain('new price version');
    d.fill('hourlyRate', '3.5');
    d.fill('dailyCap', '24');
    await d.submit();

    expect(setRule).toHaveBeenCalledWith('Standard', {
      hourlyRate: 3.5,
      dailyCap: 24,
      freeMinutes: 15,
      effectiveFromUtc: null,
    });
    expect(d.close).toHaveBeenCalledWith(true);
  });

  it('refuses a daily maximum below the hourly price', async () => {
    const setRule = vi.fn();
    const d = await open(PricingDialog, { mode: 'rule', rule }, [
      { provide: PricingApi, useValue: { setRule } },
    ]);
    d.fill('hourlyRate', '5');
    d.fill('dailyCap', '4');
    await d.submit();

    expect(setRule).not.toHaveBeenCalled();
    expect(d.el.textContent).toContain('cannot be lower than the hourly price');
  });

  it('re-checks the cap when the hourly price changes, in either direction', async () => {
    const d = await open(PricingDialog, { mode: 'rule', rule }, [
      { provide: PricingApi, useValue: { setRule: vi.fn() } },
    ]);
    const cap = d.form().controls['dailyCap'];

    d.fill('hourlyRate', '25'); // above the (unchanged) cap of 20
    expect(cap.hasError('capBelowRate')).toBe(true);

    d.fill('hourlyRate', '3'); // back below it
    expect(cap.hasError('capBelowRate')).toBe(false);
  });

  it.each([
    ['hourlyRate', '1001'],
    ['hourlyRate', '-1'],
    ['hourlyRate', '2.005'],
    ['freeMinutes', '241'],
    ['freeMinutes', '1.5'],
  ])('refuses %s = %s', async (field, value) => {
    const setRule = vi.fn();
    const d = await open(PricingDialog, { mode: 'rule', rule }, [
      { provide: PricingApi, useValue: { setRule } },
    ]);
    d.fill(field, value);
    await d.submit();
    expect(setRule).not.toHaveBeenCalled();
  });

  it('can schedule the change: the local start time is sent as UTC', async () => {
    const setRule = vi.fn().mockReturnValue(of({}));
    const d = await open(PricingDialog, { mode: 'rule', rule }, [
      { provide: PricingApi, useValue: { setRule } },
    ]);
    d.form().patchValue({ startLater: true, startAt: '2030-06-01T09:00' });
    d.fixture.detectChanges();
    await d.submit();

    expect(setRule.mock.calls[0][1].effectiveFromUtc).toBe(
      new Date(2030, 5, 1, 9, 0).toISOString(),
    );
  });

  it('edits the lost-ticket fee (only the amount is validated)', async () => {
    const setLostTicketFee = vi.fn().mockReturnValue(of({}));
    const d = await open(PricingDialog, { mode: 'lost-ticket', current: 50 }, [
      { provide: PricingApi, useValue: { setLostTicketFee } },
    ]);
    expect(d.el.querySelector('h2')?.textContent).toContain('Lost-ticket fee');
    expect(d.el.querySelector('input[formControlName=hourlyRate]')).toBeNull();
    d.fill('amount', '65');
    await d.submit();

    expect(setLostTicketFee).toHaveBeenCalledWith(65, null);
  });

  it("shows the server's refusal (e.g. a start time in the past)", async () => {
    const setRule = vi
      .fn()
      .mockReturnValue(throwError(() => problem(400, 'A price change cannot start in the past.')));
    const d = await open(PricingDialog, { mode: 'rule', rule }, [
      { provide: PricingApi, useValue: { setRule } },
    ]);
    await d.submit();
    expect(d.el.querySelector('[role=alert]')?.textContent).toContain('cannot start in the past');
    expect(d.close).not.toHaveBeenCalled();
  });
});

describe('UserDialog', () => {
  const fillValid = (d: Awaited<ReturnType<typeof open>>) => {
    d.fill('fullName', 'Sam Attendant');
    d.fill('email', 'sam@parkwise.local');
    d.fill('password', 'Passw0rd1');
  };

  it('creates a user with the chosen role (Attendant by default)', async () => {
    const create = vi.fn().mockReturnValue(of({}));
    const d = await open(UserDialog, null, [{ provide: UsersApi, useValue: { create } }]);
    fillValid(d);
    await d.submit();

    expect(create).toHaveBeenCalledWith({
      fullName: 'Sam Attendant',
      email: 'sam@parkwise.local',
      password: 'Passw0rd1',
      role: 'Attendant',
    });
    expect(d.close).toHaveBeenCalledWith(true);
  });

  it('validates name, email and password before calling the server', async () => {
    const create = vi.fn();
    const d = await open(UserDialog, null, [{ provide: UsersApi, useValue: { create } }]);
    await d.submit();
    expect(create).not.toHaveBeenCalled();
    expect(d.el.textContent).toContain('Enter a name.');
    expect(d.el.textContent).toContain('Enter an email address.');
    expect(d.el.textContent).toContain('Enter a password.');

    d.fill('fullName', 'X');
    d.fill('email', 'nope');
    d.fill('password', 'weak');
    await d.submit();
    expect(d.el.textContent).toContain('Enter a valid email address.');
    expect(d.el.textContent).toContain('does not meet the rules');
  });

  it('shows "email already used" on the email field', async () => {
    const create = vi
      .fn()
      .mockReturnValue(
        throwError(() =>
          problem(400, 'Invalid', { email: ['An account with this email already exists.'] }),
        ),
      );
    const d = await open(UserDialog, null, [{ provide: UsersApi, useValue: { create } }]);
    fillValid(d);
    await d.submit();
    expect(d.el.textContent).toContain('An account with this email already exists.');
  });

  it('lets the admin reveal the password they are typing', async () => {
    const d = await open(UserDialog, null, [{ provide: UsersApi, useValue: { create: vi.fn() } }]);
    const input = d.el.querySelector<HTMLInputElement>('input[formControlName=password]')!;
    expect(input.type).toBe('password');
    d.el.querySelector<HTMLButtonElement>('button[aria-pressed]')!.click();
    d.fixture.detectChanges();
    expect(input.type).toBe('text');
  });
});
