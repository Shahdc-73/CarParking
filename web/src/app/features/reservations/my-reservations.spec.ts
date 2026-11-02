import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Observable, of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { vi } from 'vitest';
import { PagedResult } from '../../core/api/paging';
import { ReservationQuery, ReservationsApi } from '../../core/api/reservations.api';
import { Notify } from '../../core/ui/notify';
import { Reservation, ReservationStatus } from '../../shared/models/reservation.models';
import { Confirm } from '../../shared/ui/confirm-dialog';
import { MyReservations } from './my-reservations';

const r = (id: string, status: ReservationStatus): Reservation => ({
  id,
  vehicleId: 'v',
  plateNumber: 'ABC123',
  lotId: 'l',
  lotName: 'Downtown',
  levelNumber: 1,
  spotId: 's',
  spotCode: 'D1-0' + id,
  spotType: 'Standard',
  startUtc: '2030-06-01T10:00:00Z',
  endUtc: '2030-06-01T12:00:00Z',
  holdExpiresAtUtc: '2030-06-01T10:15:00Z',
  status,
  bookedByUserId: 'u',
});
const page = (items: Reservation[], total = items.length): PagedResult<Reservation> => ({
  items,
  page: 1,
  pageSize: 10,
  totalCount: total,
  totalPages: Math.ceil(total / 10),
});

describe('MyReservations', () => {
  let list: ReturnType<typeof vi.fn<(q: ReservationQuery) => Observable<PagedResult<Reservation>>>>;
  let cancel: ReturnType<typeof vi.fn>;
  let open: ReturnType<typeof vi.fn>;
  let ask: ReturnType<typeof vi.fn<(o: unknown) => Observable<boolean>>>;
  let success: ReturnType<typeof vi.fn>;
  let error: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<MyReservations>;
  let el: HTMLElement;

  const make = async (items: Reservation[], dialogResult: Reservation | undefined = undefined) => {
    list = vi.fn().mockReturnValue(of(page(items)));
    cancel = vi.fn().mockReturnValue(of(undefined));
    open = vi.fn().mockReturnValue({ afterClosed: () => of(dialogResult) });
    ask = vi.fn().mockReturnValue(of(true));
    success = vi.fn();
    error = vi.fn();
    await TestBed.configureTestingModule({
      imports: [MyReservations],
      providers: [
        { provide: ReservationsApi, useValue: { list, cancel } },
        { provide: MatDialog, useValue: { open } },
        { provide: Confirm, useValue: { ask } },
        { provide: Notify, useValue: { success, error } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(MyReservations);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  const rows = () => [...el.querySelectorAll('tbody tr')];

  it('loads newest-first and shows where, which vehicle and a status with words and an icon', async () => {
    await make([r('1', 'Pending'), r('2', 'Fulfilled'), r('3', 'Expired'), r('4', 'Cancelled')]);

    expect(list).toHaveBeenCalledWith({ status: '', sort: 'start_desc', page: 1, pageSize: 10 });
    expect(rows().length).toBe(4);
    expect(rows()[0].textContent).toContain('Downtown');
    expect(rows()[0].textContent).toContain('spot D1-01');
    const statuses = rows().map((row) => row.querySelector('app-chip')!.textContent!.trim());
    expect(statuses).toEqual([
      expect.stringContaining('Upcoming'),
      expect.stringContaining('Used'),
      expect.stringContaining('Missed'),
      expect.stringContaining('Cancelled'),
    ]);
  });

  it('offers Cancel only for upcoming reservations', async () => {
    await make([r('1', 'Pending'), r('2', 'Fulfilled'), r('3', 'Cancelled')]);
    const cancelButtons = rows().map((row) => row.querySelector('.actions button'));
    expect(cancelButtons[0]).not.toBeNull();
    expect(cancelButtons[1]).toBeNull();
    expect(cancelButtons[2]).toBeNull();
  });

  it("shows when an upcoming reservation's hold ends", async () => {
    await make([r('1', 'Pending'), r('2', 'Fulfilled')]);
    expect(rows()[0].textContent).toContain('Held until');
    expect(rows()[1].textContent).not.toContain('Held until');
  });

  it('asks before cancelling and only cancels when confirmed', async () => {
    await make([r('1', 'Pending')]);
    ask.mockReturnValue(of(false));
    rows()[0].querySelector<HTMLButtonElement>('.actions button')!.click();
    expect(cancel).not.toHaveBeenCalled();

    ask.mockReturnValue(of(true));
    rows()[0].querySelector<HTMLButtonElement>('.actions button')!.click();
    expect(cancel).toHaveBeenCalledWith('1');
    expect(success).toHaveBeenCalledWith('Reservation cancelled.');
    expect(list).toHaveBeenCalledTimes(2); // reloaded
  });

  it('reports why a cancel failed', async () => {
    await make([r('1', 'Pending')]);
    cancel.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { title: 'A Fulfilled reservation cannot be cancelled.' },
          }),
      ),
    );
    rows()[0].querySelector<HTMLButtonElement>('.actions button')!.click();
    expect(error).toHaveBeenCalledWith('A Fulfilled reservation cannot be cancelled.');
  });

  it('reloads from page one when the status filter changes', async () => {
    await make([r('1', 'Pending')]);
    fixture.componentInstance['pageIndex'].set(3);

    fixture.componentInstance['setFilter']('Cancelled');

    expect(list).toHaveBeenLastCalledWith({
      status: 'Cancelled',
      sort: 'start_desc',
      page: 1,
      pageSize: 10,
    });
  });

  it('asks the server for the right page when paging', async () => {
    await make([r('1', 'Pending')]);
    fixture.componentInstance['onPage']({ pageIndex: 2, pageSize: 25, length: 100 });
    expect(list).toHaveBeenLastCalledWith({
      status: '',
      sort: 'start_desc',
      page: 3,
      pageSize: 25,
    });
  });

  it('shows a different empty message when a filter hides everything', async () => {
    await make([]);
    expect(el.textContent).toContain('Reserve a spot to be sure there is room');

    list.mockReturnValue(of(page([])));
    fixture.componentInstance['setFilter']('Expired');
    fixture.detectChanges();
    expect(el.textContent).toContain('Nothing matches this filter.');
  });

  it('refreshes and confirms after a new reservation is made', async () => {
    await make([r('1', 'Pending')], { ...r('9', 'Pending'), spotCode: 'A-09' });
    el.querySelector<HTMLButtonElement>('.toolbar-row button')!.click();

    expect(success).toHaveBeenCalledWith('Reserved spot A-09 at Downtown.');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('shows the date once when a reservation starts and ends on the same day, twice when it spans days', async () => {
    await make([r('1', 'Pending')]);
    const c = fixture.componentInstance as unknown as { sameDay(a: string, b: string): boolean };
    expect(c.sameDay('2030-06-01T10:00:00Z', '2030-06-01T11:00:00Z')).toBe(true);
    expect(c.sameDay('2030-06-01T10:00:00Z', '2030-06-03T11:00:00Z')).toBe(false);
  });
});
