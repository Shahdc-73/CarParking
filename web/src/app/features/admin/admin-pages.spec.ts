import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AuditApi, LotsAdminApi, UsersApi } from '../../core/api/admin.api';
import { PricingApi } from '../../core/api/pricing.api';
import { AuthService } from '../../core/auth/auth.service';
import { Notify } from '../../core/ui/notify';
import { AdminUser, AuditLog, LotDetail } from '../../shared/models/admin.models';
import { PricingOverview } from '../../shared/models/pricing.models';
import { Confirm } from '../../shared/ui/confirm-dialog';
import { BoardSpot, Lot, LotBoard, SpotStatus } from '../parking/parking.models';
import { ParkingService } from '../parking/parking.service';
import { AdminAudit } from './admin-audit';
import { AdminLotDetail } from './admin-lot-detail';
import { AdminLots } from './admin-lots';
import { AdminPricing } from './admin-pricing';
import { AdminUsers } from './admin-users';
import { PricingDialog } from './pricing-dialog';

const page = <T>(items: T[], total = items.length) => ({
  items,
  page: 1,
  pageSize: 10,
  totalCount: total,
  totalPages: Math.ceil(total / 10),
});
const lot = (id: string, name: string, isActive = true): Lot => ({
  id,
  name,
  address: '1 Road',
  isActive,
  allowMotorcycleOverflow: false,
  levelCount: 2,
  spotCount: 20,
});
const err = (status: number, title: string) => new HttpErrorResponse({ status, error: { title } });

/** Shared stubs: every page asks the same three collaborators for notifications, confirmation and dialogs. */
function collaborators(confirmResult = true, dialogResult: unknown = undefined) {
  const success = vi.fn();
  const error = vi.fn();
  const ask = vi.fn().mockReturnValue(of(confirmResult));
  const open = vi.fn().mockReturnValue({ afterClosed: () => of(dialogResult) });
  return {
    success,
    error,
    ask,
    open,
    providers: [
      { provide: Notify, useValue: { success, error } },
      { provide: Confirm, useValue: { ask } },
      { provide: MatDialog, useValue: { open } },
    ],
  };
}

const click = (el: Element | null | undefined) => (el as HTMLElement).click();
const find = (root: HTMLElement, label: string) =>
  root.querySelector<HTMLButtonElement>(`[aria-label="${label}"]`);

describe('AdminLots', () => {
  let list: ReturnType<typeof vi.fn<(...a: unknown[]) => Observable<ReturnType<typeof page<Lot>>>>>;
  let setActive: ReturnType<typeof vi.fn>;
  let remove: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<AdminLots>;
  let el: HTMLElement;
  let c: ReturnType<typeof collaborators>;

  const make = async (lots: Lot[], confirmResult = true, dialogResult: unknown = undefined) => {
    c = collaborators(confirmResult, dialogResult);
    list = vi.fn().mockReturnValue(of(page(lots)));
    setActive = vi.fn().mockReturnValue(of(undefined));
    remove = vi.fn().mockReturnValue(of(undefined));
    await TestBed.configureTestingModule({
      imports: [AdminLots],
      providers: [
        provideRouter([]),
        { provide: LotsAdminApi, useValue: { list, setActive, delete: remove } },
        ...c.providers,
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminLots);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  it('lists lots with counts, an open/closed status and a "Manage" link', async () => {
    await make([lot('l1', 'Downtown'), lot('l2', 'Old Lot', false)]);

    const rows = el.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Downtown');
    expect(rows[0].querySelector('app-chip')?.textContent).toContain('Open');
    expect(rows[1].querySelector('app-chip')?.textContent).toContain('Closed');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe('/admin/lots/l1');
    expect(rows[0].querySelector('a')?.getAttribute('aria-label')).toBe(
      'Manage levels and spots of Downtown',
    );
  });

  it('searches from page one', async () => {
    await make([lot('l1', 'Downtown')]);
    fixture.componentInstance['pageIndex'].set(2);
    fixture.componentInstance['search'].set('air');
    fixture.componentInstance['find']();
    expect(list).toHaveBeenLastCalledWith('air', 1, 10);
  });

  it('opens the create dialog and confirms with a pointer to the next step', async () => {
    await make([lot('l1', 'Downtown')], true, true);
    click(
      el.querySelector(
        '.toolbar-row button.mdc-button--unelevated, .toolbar-row button:last-of-type',
      ),
    );
    expect(c.open).toHaveBeenCalled();
    expect(c.success).toHaveBeenCalledWith('Lot created. Add levels and spots next.');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('closing a lot asks first, and says what will be refused', async () => {
    await make([lot('l1', 'Downtown')]);
    click(find(el, 'Close Downtown'));

    expect(c.ask).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Close Downtown?',
        destructive: true,
        message: expect.stringContaining('refused while cars are parked'),
      }),
    );
    expect(setActive).toHaveBeenCalledWith('l1', false);
    expect(c.success).toHaveBeenCalledWith('Downtown closed.');
  });

  it('reopens a closed lot without a scary confirmation', async () => {
    await make([lot('l2', 'Old Lot', false)]);
    click(find(el, 'Reopen Old Lot'));
    expect(c.ask).toHaveBeenCalledWith(expect.objectContaining({ destructive: false }));
    expect(setActive).toHaveBeenCalledWith('l2', true);
  });

  it('does nothing when the confirmation is declined', async () => {
    await make([lot('l1', 'Downtown')], false);
    click(find(el, 'Close Downtown'));
    click(find(el, 'Delete Downtown'));
    expect(setActive).not.toHaveBeenCalled();
    expect(remove).not.toHaveBeenCalled();
  });

  it("explains why a lot could not be closed or deleted (the server's reason)", async () => {
    await make([lot('l1', 'Downtown')]);
    setActive.mockReturnValue(
      throwError(() => err(409, 'Cannot deactivate a lot while vehicles are parked in it.')),
    );
    remove.mockReturnValue(
      throwError(() =>
        err(409, 'This lot has parking history and cannot be deleted. Deactivate it instead.'),
      ),
    );

    click(find(el, 'Close Downtown'));
    click(find(el, 'Delete Downtown'));

    expect(c.error).toHaveBeenCalledWith(
      'Cannot deactivate a lot while vehicles are parked in it.',
    );
    expect(c.error).toHaveBeenCalledWith(
      'This lot has parking history and cannot be deleted. Deactivate it instead.',
    );
  });

  it('deletes after confirmation and reloads', async () => {
    await make([lot('l1', 'Downtown')]);
    click(find(el, 'Delete Downtown'));
    expect(remove).toHaveBeenCalledWith('l1');
    expect(c.success).toHaveBeenCalledWith('Downtown deleted.');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('shows an empty state with the right invitation', async () => {
    await make([]);
    expect(el.textContent).toContain('Create the first lot.');
  });
});

describe('AdminLotDetail', () => {
  const detail: LotDetail = {
    id: 'l1',
    name: 'Downtown',
    address: '12 Main',
    isActive: true,
    allowMotorcycleOverflow: false,
    levels: [
      { id: 'lv1', lotId: 'l1', number: 1, name: 'Ground', spotCount: 3 },
      { id: 'lv2', lotId: 'l1', number: 2, name: 'Roof', spotCount: 0 },
    ],
  };
  const spot = (code: string, status: SpotStatus): BoardSpot => ({
    id: 's-' + code,
    code,
    type: 'Standard',
    status,
    occupancy: null,
    holdExpiresAtUtc: null,
  });
  const board: LotBoard = {
    lotId: 'l1',
    lotName: 'Downtown',
    isActive: true,
    generatedAtUtc: new Date().toISOString(),
    totals: { total: 3, available: 1, occupied: 1, reserved: 0, outOfService: 1 },
    levels: [
      {
        id: 'lv1',
        number: 1,
        name: 'Ground',
        spots: [spot('A-01', 'Available'), spot('A-02', 'Occupied'), spot('A-03', 'OutOfService')],
      },
      { id: 'lv2', number: 2, name: 'Roof', spots: [] },
    ],
  };

  let api: Record<string, ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<AdminLotDetail>;
  let el: HTMLElement;
  let c: ReturnType<typeof collaborators>;

  const make = async (confirmResult = true, dialogResult: unknown = undefined) => {
    c = collaborators(confirmResult, dialogResult);
    api = {
      get: vi.fn().mockReturnValue(of(detail)),
      setSpotService: vi.fn().mockReturnValue(of({})),
      deleteSpot: vi.fn().mockReturnValue(of(undefined)),
      deleteLevel: vi.fn().mockReturnValue(of(undefined)),
    };
    await TestBed.configureTestingModule({
      imports: [AdminLotDetail],
      providers: [
        provideRouter([]),
        { provide: LotsAdminApi, useValue: api },
        { provide: ParkingService, useValue: { getBoard: () => of(board) } },
        ...c.providers,
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminLotDetail);
    fixture.componentRef.setInput('id', 'l1');
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  it('shows each level with its spots, types and statuses (with words, not just colour)', async () => {
    await make();

    expect(el.querySelector('h1')?.textContent).toBe('Downtown');
    const levels = el.querySelectorAll('.level');
    expect(levels.length).toBe(2);
    const rows = levels[0].querySelectorAll('tbody tr');
    expect(rows.length).toBe(3);
    expect(
      [...rows].map((r) => r.querySelector('app-chip')!.textContent!.replace(/\s+/g, ' ').trim()),
    ).toEqual([
      expect.stringContaining('Free'),
      expect.stringContaining('Occupied'),
      expect.stringContaining('Out of service'),
    ]);
    expect(levels[1].textContent).toContain('No spots on this level yet.');
  });

  it('cannot take an occupied spot out of service (the button is disabled)', async () => {
    await make();
    expect(find(el, 'Take out of service spot A-02')!.disabled).toBe(true);
    expect(find(el, 'Take out of service spot A-01')!.disabled).toBe(false);
  });

  it('takes a free spot out of service and brings an out-of-service one back', async () => {
    await make();
    click(find(el, 'Take out of service spot A-01'));
    expect(api['setSpotService']).toHaveBeenCalledWith('s-A-01', false);
    expect(c.success).toHaveBeenCalledWith('Spot A-01 is out of service.');

    click(find(el, 'Return spot A-03'));
    expect(api['setSpotService']).toHaveBeenCalledWith('s-A-03', true);
    expect(c.success).toHaveBeenCalledWith('Spot A-03 is back in service.');
  });

  it('deletes a spot only after confirmation, and reports a refusal', async () => {
    await make();
    api['deleteSpot'].mockReturnValue(
      throwError(() =>
        err(
          409,
          'This spot has parking history and cannot be deleted. Take it out of service instead.',
        ),
      ),
    );
    click(find(el, 'Delete spot A-01'));

    expect(c.ask).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'Delete spot A-01?', destructive: true }),
    );
    expect(c.error).toHaveBeenCalledWith(expect.stringContaining('Take it out of service instead'));
  });

  it('does not delete anything when the confirmation is declined', async () => {
    await make(false);
    click(find(el, 'Delete spot A-01'));
    click(find(el, 'Delete level Ground'));
    expect(api['deleteSpot']).not.toHaveBeenCalled();
    expect(api['deleteLevel']).not.toHaveBeenCalled();
  });

  it('suggests the next free level number when adding a level', async () => {
    await make();
    click(
      [...el.querySelectorAll('app-page-header button')].find((b) =>
        b.textContent!.includes('Add level'),
      ),
    );
    expect(c.open.mock.calls[0][1].data).toEqual({ mode: 'add', lotId: 'l1', nextNumber: 3 });
  });

  it('opens the spot dialog for the right level and reloads after a save', async () => {
    await make(true, true);
    click([...el.querySelectorAll('.level')][0].querySelector('.level-actions button'));
    expect(c.open.mock.calls[0][1].data).toEqual({
      mode: 'add',
      levelId: 'lv1',
      levelName: 'Ground',
    });
    expect(c.success).toHaveBeenCalledWith('Spot added.');
    expect(api['get']).toHaveBeenCalledTimes(2);
  });
});

describe('AdminPricing', () => {
  const pricing: PricingOverview = {
    rules: [
      {
        spotType: 'Standard',
        hourlyRate: 3,
        dailyCap: 20,
        freeMinutes: 15,
        effectiveFromUtc: '2025-01-01T00:00:00Z',
      },
      {
        spotType: 'EV',
        hourlyRate: 3.5,
        dailyCap: 24,
        freeMinutes: 15,
        effectiveFromUtc: '2025-01-01T00:00:00Z',
      },
    ],
    lostTicketFee: { amount: 50, effectiveFromUtc: '2025-01-01T00:00:00Z' },
  };
  let fixture: ComponentFixture<AdminPricing>;
  let el: HTMLElement;
  let current: ReturnType<typeof vi.fn>;
  let c: ReturnType<typeof collaborators>;

  const make = async (value: PricingOverview = pricing, dialogResult: unknown = undefined) => {
    c = collaborators(true, dialogResult);
    current = vi.fn().mockReturnValue(of(value));
    await TestBed.configureTestingModule({
      imports: [AdminPricing],
      providers: [{ provide: PricingApi, useValue: { current } }, ...c.providers],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminPricing);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  it('shows the price of each spot type with two decimals, and the lost-ticket fee', async () => {
    await make();
    const rows = el.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[1].textContent).toContain('EV');
    expect(rows[1].textContent).toContain('3.50');
    expect(rows[1].textContent).toContain('24.00');
    expect(el.querySelector('.lost strong')?.textContent).toContain('50.00');
    expect(el.textContent).toContain('never changes a receipt');
  });

  it('opens the price dialog for the chosen spot type and reloads after saving', async () => {
    await make(pricing, true);
    click(find(el, 'Change the price for EV spots'));

    expect(c.open).toHaveBeenCalledWith(
      PricingDialog,
      expect.objectContaining({ data: { mode: 'rule', rule: pricing.rules[1] } }),
    );
    expect(c.success).toHaveBeenCalledWith('Price saved.');
    expect(current).toHaveBeenCalledTimes(2);
  });

  it('edits the lost-ticket fee', async () => {
    await make();
    click(find(el, 'Change the lost-ticket fee'));
    expect(c.open.mock.calls[0][1].data).toEqual({ mode: 'lost-ticket', current: 50 });
  });

  it('says "Not set" when no lost-ticket fee exists', async () => {
    await make({ ...pricing, lostTicketFee: null });
    expect(el.querySelector('.lost strong')?.textContent).toBe('Not set');
  });
});

describe('AdminUsers', () => {
  const user = (id: string, name: string, extra: Partial<AdminUser> = {}): AdminUser => ({
    id,
    email: `${name.toLowerCase()}@x.y`,
    fullName: name,
    roles: ['Attendant'],
    isActive: true,
    ...extra,
  });
  let list: ReturnType<typeof vi.fn>;
  let setActive: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<AdminUsers>;
  let el: HTMLElement;
  let c: ReturnType<typeof collaborators>;

  const make = async (users: AdminUser[], confirmResult = true) => {
    c = collaborators(confirmResult, true);
    list = vi.fn().mockReturnValue(of(page(users)));
    setActive = vi.fn().mockReturnValue(of(undefined));
    await TestBed.configureTestingModule({
      imports: [AdminUsers],
      providers: [
        { provide: UsersApi, useValue: { list, setActive } },
        {
          provide: AuthService,
          useValue: { user: signal({ id: 'me', fullName: 'Alex Admin' }).asReadonly() },
        },
        ...c.providers,
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminUsers);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  it('lists users sorted by name with role and status', async () => {
    await make([user('u1', 'Sam'), user('u2', 'Dana', { roles: ['Driver'], isActive: false })]);

    expect(list).toHaveBeenCalledWith({
      search: '',
      role: '',
      sort: 'name',
      page: 1,
      pageSize: 10,
    });
    const rows = el.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Attendant');
    expect(rows[0].querySelector('app-chip')?.textContent).toContain('Active');
    expect(rows[1].querySelector('app-chip')?.textContent).toContain('Deactivated');
  });

  it('marks the signed-in admin and will not let them deactivate themselves', async () => {
    await make([user('me', 'Alex', { roles: ['Admin'] }), user('u1', 'Sam')]);

    const self = find(el, 'Deactivate Alex')!;
    expect(self.disabled).toBe(true);
    expect(self.getAttribute('title')).toBe('You cannot deactivate your own account.');
    expect(el.querySelector('tbody tr')?.textContent).toContain('(you)');
    expect(find(el, 'Deactivate Sam')!.disabled).toBe(false);
  });

  it('deactivates another user after confirmation, then reloads', async () => {
    await make([user('u1', 'Sam')]);
    click(find(el, 'Deactivate Sam'));

    expect(c.ask).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'Deactivate Sam?', destructive: true }),
    );
    expect(setActive).toHaveBeenCalledWith('u1', false);
    expect(c.success).toHaveBeenCalledWith('Sam deactivated.');
  });

  it('reactivates a deactivated user', async () => {
    await make([user('u2', 'Dana', { isActive: false })]);
    click(find(el, 'Reactivate Dana'));
    expect(setActive).toHaveBeenCalledWith('u2', true);
  });

  it("shows the server's reason if deactivation is refused", async () => {
    await make([user('u1', 'Sam')]);
    setActive.mockReturnValue(
      throwError(() => err(409, 'You cannot deactivate your own account.')),
    );
    click(find(el, 'Deactivate Sam'));
    expect(c.error).toHaveBeenCalledWith('You cannot deactivate your own account.');
  });

  it('filters by role from page one', async () => {
    await make([user('u1', 'Sam')]);
    fixture.componentInstance['pageIndex'].set(3);
    fixture.componentInstance['role'].set('Driver');
    fixture.componentInstance['find']();
    expect(list).toHaveBeenLastCalledWith({
      search: '',
      role: 'Driver',
      sort: 'name',
      page: 1,
      pageSize: 10,
    });
  });

  it('confirms creation and reminds the admin what to hand over', async () => {
    await make([user('u1', 'Sam')]);
    click(el.querySelector('.toolbar-row button:last-of-type'));
    expect(c.success).toHaveBeenCalledWith(
      'User created. Give them their email and temporary password.',
    );
  });
});

describe('AdminAudit', () => {
  const log = (id: string, extra: Partial<AuditLog> = {}): AuditLog => ({
    id,
    actorUserId: '0198a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b',
    action: 'Spot.TakenOutOfService',
    entityType: 'Spot',
    entityId: '0123abcd-0000-0000-0000-000000000000',
    beforeJson: '{"Status":"Available"}',
    afterJson: '{"Status":"OutOfService"}',
    timestampUtc: '2026-06-01T10:00:00Z',
    ...extra,
  });
  let list: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<AdminAudit>;
  let el: HTMLElement;

  const make = async (items: AuditLog[]) => {
    list = vi.fn().mockReturnValue(of(page(items)));
    await TestBed.configureTestingModule({
      imports: [AdminAudit],
      providers: [{ provide: AuditApi, useValue: { list } }],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminAudit);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  it('shows who did what, in words, with the ids shortened', async () => {
    await make([log('1')]);
    const row = el.querySelector('tbody tr')!;
    expect(row.textContent).toContain('Spot: taken out of service');
    expect(row.querySelectorAll('code')[0].textContent).toBe('0198a1b2');
    expect(row.querySelectorAll('code')[1].textContent).toBe('0123abcd');
  });

  it('names the system when there is no actor (the background job)', async () => {
    await make([log('1', { actorUserId: null, action: 'Reservation.Expired' })]);
    expect(el.querySelector('tbody tr em')?.textContent).toBe('System');
  });

  it('offers the before/after snapshots as readable JSON, collapsed until asked for', async () => {
    await make([log('1')]);
    const details = el.querySelector('details')!;
    expect(details.open).toBe(false);
    const blocks = [...details.querySelectorAll('pre')].map((p) => p.textContent);
    expect(blocks[0]).toBe('{\n  "Status": "Available"\n}');
    expect(blocks[1]).toBe('{\n  "Status": "OutOfService"\n}');
  });

  it('shows a dash when there is nothing to compare', async () => {
    await make([log('1', { beforeJson: null, afterJson: null })]);
    expect(el.querySelector('details')).toBeNull();
  });

  it('converts the chosen LOCAL days to UTC instants, including the whole last day', async () => {
    await make([log('1')]);
    const c = fixture.componentInstance as unknown as {
      from: { set(v: string): void };
      to: { set(v: string): void };
      apply(): void;
    };
    c.from.set('2026-06-01');
    c.to.set('2026-06-03');
    c.apply();

    expect(list).toHaveBeenLastCalledWith(
      expect.objectContaining({
        fromUtc: new Date(2026, 5, 1).toISOString(),
        toUtc: new Date(2026, 5, 4).toISOString(),
        page: 1,
      }),
    );
  });

  it('filters by entity and action, and clears everything with one button', async () => {
    await make([log('1')]);
    const c = fixture.componentInstance as unknown as {
      entityType: { set(v: string): void };
      action: { set(v: string): void };
      apply(): void;
      reset(): void;
    };
    c.entityType.set('Spot');
    c.action.set('Spot.Created');
    c.apply();
    expect(list).toHaveBeenLastCalledWith(
      expect.objectContaining({ entityType: 'Spot', action: 'Spot.Created' }),
    );

    c.reset();
    expect(list).toHaveBeenLastCalledWith(
      expect.objectContaining({ entityType: '', action: '', fromUtc: '', toUtc: '' }),
    );
  });

  it('shows an empty state when nothing matches', async () => {
    await make([]);
    expect(el.textContent).toContain('No changes match these filters.');
  });
});
