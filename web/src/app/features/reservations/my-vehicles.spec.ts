import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { VehiclesApi } from '../../core/api/vehicles.api';
import { Notify } from '../../core/ui/notify';
import { Vehicle } from '../../shared/models/vehicle.models';
import { Confirm } from '../../shared/ui/confirm-dialog';
import { MyVehicles } from './my-vehicles';

const v = (id: string, plate: string, extra: Partial<Vehicle> = {}): Vehicle => ({
  id,
  plateNumber: plate,
  type: 'Car',
  color: 'Blue',
  hasDisabledPermit: false,
  ownerUserId: 'u',
  ...extra,
});
const page = (items: Vehicle[]) => ({
  items,
  page: 1,
  pageSize: 100,
  totalCount: items.length,
  totalPages: 1,
});

describe('MyVehicles', () => {
  let list: ReturnType<typeof vi.fn<() => Observable<ReturnType<typeof page>>>>;
  let remove: ReturnType<typeof vi.fn>;
  let open: ReturnType<typeof vi.fn>;
  let ask: ReturnType<typeof vi.fn<(o: unknown) => Observable<boolean>>>;
  let success: ReturnType<typeof vi.fn>;
  let error: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<MyVehicles>;
  let el: HTMLElement;

  const make = async (items: Vehicle[], dialogResult: Vehicle | undefined = undefined) => {
    list = vi.fn().mockReturnValue(of(page(items)));
    remove = vi.fn().mockReturnValue(of(undefined));
    open = vi.fn().mockReturnValue({ afterClosed: () => of(dialogResult) });
    ask = vi.fn().mockReturnValue(of(true));
    success = vi.fn();
    error = vi.fn();
    await TestBed.configureTestingModule({
      imports: [MyVehicles],
      providers: [
        { provide: VehiclesApi, useValue: { list, delete: remove } },
        { provide: MatDialog, useValue: { open } },
        { provide: Confirm, useValue: { ask } },
        { provide: Notify, useValue: { success, error } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(MyVehicles);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  it('shows each vehicle with its plate, type, colour and a disabled-permit chip when it has one', async () => {
    await make([
      v('1', 'ABC123'),
      v('2', 'MOTO77', { type: 'Motorcycle', color: 'Red', hasDisabledPermit: true }),
    ]);

    const cards = el.querySelectorAll('.vehicle');
    expect(cards.length).toBe(2);
    expect(cards[0].textContent).toContain('ABC123');
    expect(cards[0].textContent).toContain('Car · Blue');
    expect(cards[0].textContent).not.toContain('Disabled permit');
    expect(cards[1].textContent).toContain('Motorcycle · Red');
    expect(cards[1].textContent).toContain('Disabled permit');
  });

  it('gives every action button a name that says which vehicle it is for', async () => {
    await make([v('1', 'ABC123')]);
    const labels = [...el.querySelectorAll('.vehicle button')].map((b) =>
      b.getAttribute('aria-label'),
    );
    expect(labels).toEqual(['Edit ABC123', 'Remove ABC123']);
  });

  it('invites the driver to add their first vehicle', async () => {
    await make([]);
    expect(el.textContent).toContain('No vehicles yet');
  });

  it('reloads and confirms after a vehicle is added', async () => {
    await make([v('1', 'ABC123')], v('2', 'NEW777'));
    el.querySelector<HTMLButtonElement>('.toolbar-row button')!.click();

    expect(success).toHaveBeenCalledWith('NEW777 added.');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('does nothing when the add dialog is dismissed', async () => {
    await make([v('1', 'ABC123')], undefined);
    el.querySelector<HTMLButtonElement>('.toolbar-row button')!.click();
    expect(success).not.toHaveBeenCalled();
    expect(list).toHaveBeenCalledTimes(1);
  });

  it('edits a vehicle by opening the dialog with it', async () => {
    const vehicle = v('1', 'ABC123');
    await make([vehicle], { ...vehicle, color: 'Red' });
    el.querySelector<HTMLButtonElement>('button[aria-label="Edit ABC123"]')!.click();

    expect(open.mock.calls[0][1].data).toEqual(vehicle);
    expect(success).toHaveBeenCalledWith('ABC123 updated.');
  });

  it('asks before removing, and removes only when confirmed', async () => {
    await make([v('1', 'ABC123')]);
    ask.mockReturnValue(of(false));
    el.querySelector<HTMLButtonElement>('button[aria-label="Remove ABC123"]')!.click();
    expect(remove).not.toHaveBeenCalled();

    ask.mockReturnValue(of(true));
    el.querySelector<HTMLButtonElement>('button[aria-label="Remove ABC123"]')!.click();
    expect(ask).toHaveBeenLastCalledWith(expect.objectContaining({ destructive: true }));
    expect(remove).toHaveBeenCalledWith('1');
    expect(success).toHaveBeenCalledWith('ABC123 removed.');
  });

  it('explains why a vehicle with parking history cannot be removed', async () => {
    await make([v('1', 'ABC123')]);
    remove.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { title: 'This vehicle has parking history and cannot be deleted.' },
          }),
      ),
    );
    el.querySelector<HTMLButtonElement>('button[aria-label="Remove ABC123"]')!.click();

    expect(error).toHaveBeenCalledWith('This vehicle has parking history and cannot be deleted.');
  });

  it('shows an error with a retry if the list cannot be loaded', async () => {
    list = vi.fn();
    await make([]);
    list.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    fixture.componentInstance['load']();
    fixture.detectChanges();
    expect(el.querySelector('[role=alert]')?.textContent).toMatch(/cannot reach the server/i);
  });
});
