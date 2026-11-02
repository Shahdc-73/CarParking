import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Observable, of } from 'rxjs';
import { vi } from 'vitest';
import { PagedResult } from '../../core/api/paging';
import { SessionQuery, SessionsApi } from '../../core/api/sessions.api';
import { Session } from '../../shared/models/session.models';
import { ReceiptDialog } from '../gate/receipt-dialog';
import { MyHistory } from './my-history';

const s = (id: string, extra: Partial<Session> = {}): Session => ({
  id,
  ticketCode: 'PW-' + id,
  vehicleId: 'v',
  plateNumber: 'ABC123',
  vehicleType: 'Car',
  lotId: 'l',
  lotName: 'Downtown',
  levelNumber: 1,
  spotId: 'sp',
  spotCode: 'D1-07',
  spotType: 'Standard',
  checkInAtUtc: '2030-06-01T10:00:00Z',
  checkOutAtUtc: '2030-06-01T12:30:00Z',
  status: 'Completed',
  receiptId: 'rc-' + id,
  receiptNumber: 'RC-' + id,
  amountPaid: 6,
  reservationId: null,
  ...extra,
});
const page = (items: Session[]): PagedResult<Session> => ({
  items,
  page: 1,
  pageSize: 10,
  totalCount: items.length,
  totalPages: 1,
});

describe('MyHistory', () => {
  let list: ReturnType<typeof vi.fn<(q: SessionQuery) => Observable<PagedResult<Session>>>>;
  let open: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<MyHistory>;
  let el: HTMLElement;

  const make = async (items: Session[]) => {
    list = vi.fn().mockReturnValue(of(page(items)));
    open = vi.fn();
    await TestBed.configureTestingModule({
      imports: [MyHistory],
      providers: [
        { provide: SessionsApi, useValue: { list } },
        { provide: MatDialog, useValue: { open } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(MyHistory);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  const rows = () => [...el.querySelectorAll('tbody tr')];

  it('shows each visit with the time parked, what was paid and a status', async () => {
    await make([s('1'), s('2', { status: 'LostTicket', amountPaid: 50 })]);

    expect(rows()[0].textContent).toContain('Downtown');
    expect(rows()[0].textContent).toContain('02:30:00');
    expect(rows()[0].querySelector('.num')?.textContent).toContain('6.00');
    expect(rows()[0].querySelector('app-chip')?.textContent).toContain('Completed');
    expect(rows()[1].querySelector('.num')?.textContent).toContain('50.00');
    expect(rows()[1].querySelector('app-chip')?.textContent).toContain('Lost ticket');
  });

  it('shows a car that is still parked as such, without an amount or a receipt button', async () => {
    await make([
      s('1', { status: 'Active', checkOutAtUtc: null, receiptId: null, amountPaid: null }),
    ]);

    expect(rows()[0].textContent).toContain('Still parked');
    expect(rows()[0].querySelector('.num')?.textContent).toContain('-');
    expect(rows()[0].querySelector('.actions button')).toBeNull();
    expect(rows()[0].querySelector('app-chip')?.textContent).toContain('Parked now');
  });

  it('opens the receipt of a visit, with a button named after the visit', async () => {
    await make([s('1')]);
    const button = rows()[0].querySelector<HTMLButtonElement>('.actions button')!;
    expect(button.getAttribute('aria-label')).toMatch(/^View receipt for ABC123 on /);

    button.click();
    expect(open).toHaveBeenCalledWith(
      ReceiptDialog,
      expect.objectContaining({ data: { receiptId: 'rc-1' } }),
    );
  });

  it('filters by status and goes back to the first page', async () => {
    await make([s('1')]);
    fixture.componentInstance['pageIndex'].set(2);
    fixture.componentInstance['setFilter']('LostTicket');
    expect(list).toHaveBeenLastCalledWith({ status: 'LostTicket', page: 1, pageSize: 10 });
  });

  it('shows an empty state', async () => {
    await make([]);
    expect(el.textContent).toContain('No parking visits yet');
  });
});
