import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { SessionsApi } from '../../core/api/sessions.api';
import { Notify } from '../../core/ui/notify';
import { PagedResult } from '../../core/api/paging';
import { Session } from '../../shared/models/session.models';
import { BoardSpot, LotBoard, SpotOccupancy } from '../parking/parking.models';
import { ParkingService } from '../parking/parking.service';
import { ParkedCar, ParkedList } from './parked-list';

const T0 = new Date('2026-06-01T12:00:00Z');
const ago = (minutes: number) => new Date(T0.getTime() - minutes * 60_000).toISOString();

const parked = (
  code: string,
  plate: string,
  minutesAgo: number,
  extra: Partial<SpotOccupancy> = {},
): BoardSpot => ({
  id: code,
  code,
  type: 'Standard',
  status: 'Occupied',
  holdExpiresAtUtc: null,
  occupancy: {
    sessionId: 's-' + plate,
    checkInAtUtc: ago(minutesAgo),
    plateNumber: plate,
    feeSoFar: 3,
    freeMinutes: 15,
    isMine: false,
    ...extra,
  },
});
const free = (code: string): BoardSpot => ({
  id: code,
  code,
  type: 'Standard',
  status: 'Available',
  occupancy: null,
  holdExpiresAtUtc: null,
});

const board = (spots: BoardSpot[]): LotBoard => ({
  lotId: 'lot-1',
  lotName: 'Downtown',
  isActive: true,
  generatedAtUtc: new Date(Date.now()).toISOString(),
  totals: { total: spots.length, available: 0, occupied: 0, reserved: 0, outOfService: 0 },
  levels: [{ id: 'l1', number: 1, name: 'Ground', spots }],
});

describe('ParkedList', () => {
  let getBoard: ReturnType<typeof vi.fn<(id: string) => Observable<LotBoard>>>;
  let list: ReturnType<typeof vi.fn<(q: unknown) => Observable<PagedResult<Session>>>>;
  let error: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<ParkedList>;
  let el: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(T0);
  });
  afterEach(() => {
    fixture?.destroy();
    vi.useRealTimers();
  });

  const create = async (spots: BoardSpot[]) => {
    getBoard = vi.fn().mockReturnValue(of(board(spots)));
    list = vi.fn();
    error = vi.fn();
    await TestBed.configureTestingModule({
      imports: [ParkedList],
      providers: [
        { provide: ParkingService, useValue: { getBoard } },
        { provide: SessionsApi, useValue: { list } },
        { provide: Notify, useValue: { error, success: vi.fn() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ParkedList);
    fixture.componentRef.setInput('lotId', 'lot-1');
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  const rows = () => [...el.querySelectorAll('tbody tr')];
  const plates = () => rows().map((r) => r.querySelector('th')!.textContent!.trim());
  const type = (value: string) => {
    const input = el.querySelector<HTMLInputElement>('input[type=search]')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  it('lists only the occupied spots, longest-parked first, with a live timer', async () => {
    await create([
      free('A-01'),
      parked('A-02', 'NEWER1', 20),
      parked('A-03', 'OLDEST', 300),
      parked('A-04', 'MIDDLE', 90),
    ]);

    expect(plates()).toEqual(['OLDEST', 'MIDDLE', 'NEWER1']);
    expect(rows()[0].querySelector('.clock')?.textContent).toBe('05:00:00');
    expect(el.querySelector('.count')?.textContent).toBe('3');

    vi.advanceTimersByTime(5000);
    fixture.detectChanges();
    expect(rows()[0].querySelector('.clock')?.textContent).toBe('05:00:05');
  });

  it('shows the free-period countdown first, then the fee so far', async () => {
    await create([
      parked('A-01', 'FRESH1', 5, { feeSoFar: 0 }),
      parked('A-02', 'LONGER', 120, { feeSoFar: 6 }),
    ]);

    const byPlate = (plate: string) =>
      rows().find((r) => r.querySelector('th')!.textContent!.includes(plate))!;
    expect(byPlate('FRESH1').querySelector('.free')?.textContent).toContain('Free for 00:10:00');
    expect(byPlate('LONGER').querySelectorAll('td')[2].textContent).toContain('6.00');
  });

  it('filters live by plate or spot, ignoring spaces, dashes and case', async () => {
    await create([parked('D1-07', 'ABC1234', 60), parked('D1-08', 'XYZ9087', 50)]);

    type('abc-12');
    expect(plates()).toEqual(['ABC1234']);
    type('d108');
    expect(plates()).toEqual(['XYZ9087']);
    type('nothing');
    expect(el.textContent).toContain('No parked vehicle matches that search.');
    type('');
    expect(rows().length).toBe(2);
  });

  it('shows an empty state when nothing is parked', async () => {
    await create([free('A-01')]);
    expect(el.textContent).toContain('No vehicles parked');
    expect(el.querySelector('table')).toBeNull();
  });

  it('emits the car when Check out or Lost ticket is pressed, with accessible button names', async () => {
    await create([parked('A-02', 'ABC123', 60)]);
    const out: ParkedCar[] = [];
    const lost: ParkedCar[] = [];
    fixture.componentInstance.checkOut.subscribe((c) => out.push(c));
    fixture.componentInstance.lostTicket.subscribe((c) => lost.push(c));

    const buttons = rows()[0].querySelectorAll('button');
    expect(buttons[0].getAttribute('aria-label')).toBe('Check out ABC123');
    expect(buttons[1].getAttribute('aria-label')).toBe('Lost ticket for ABC123');
    buttons[0].click();
    buttons[1].click();

    expect(out[0]).toMatchObject({
      sessionId: 's-ABC123',
      plateNumber: 'ABC123',
      spotCode: 'A-02',
    });
    expect(lost[0].plateNumber).toBe('ABC123');
  });

  describe('ticket lookup', () => {
    const enter = () =>
      el
        .querySelector('input[type=search]')!
        .dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    const found = (): Session =>
      ({ id: 'sess-9', plateNumber: 'ZZZ999', spotCode: 'A-09', checkInAtUtc: ago(10) }) as Session;

    it('looks a ticket code up exactly on the server (upper-cased) and opens it for check-out', async () => {
      await create([parked('A-02', 'ABC123', 60)]);
      list.mockReturnValue(
        of({ items: [found()], page: 1, pageSize: 1, totalCount: 1, totalPages: 1 }),
      );
      const opened: ParkedCar[] = [];
      fixture.componentInstance.ticketFound.subscribe((c) => opened.push(c));

      type('pw-k7m2qx');
      enter();

      expect(list).toHaveBeenCalledWith({ ticketCode: 'PW-K7M2QX', status: 'Active', pageSize: 1 });
      expect(opened[0]).toMatchObject({
        sessionId: 'sess-9',
        plateNumber: 'ZZZ999',
        spotCode: 'A-09',
      });
    });

    it('clears the search box once the ticket is found, so the list is not left filtered to nothing', async () => {
      await create([parked('A-02', 'ABC123', 60)]);
      list.mockReturnValue(
        of({ items: [found()], page: 1, pageSize: 1, totalCount: 1, totalPages: 1 }),
      );

      type('PW-K7M2QX');
      enter();
      fixture.detectChanges();
      await fixture.whenStable(); // ngModel writes the new value to the input asynchronously
      fixture.detectChanges();

      expect(el.querySelector<HTMLInputElement>('input[type=search]')!.value).toBe('');
      expect(rows().length).toBe(1);
    });

    it('tells the attendant when no parked vehicle has that ticket', async () => {
      await create([]);
      list.mockReturnValue(of({ items: [], page: 1, pageSize: 1, totalCount: 0, totalPages: 0 }));
      type('PW-AAAAAA');
      enter();
      expect(error).toHaveBeenCalledWith('No parked vehicle has ticket PW-AAAAAA.');
    });

    it('does not call the server for ordinary plate searches', async () => {
      await create([parked('A-02', 'ABC123', 60)]);
      type('ABC123');
      enter();
      expect(list).not.toHaveBeenCalled();
    });

    it('reports a failed lookup', async () => {
      await create([]);
      list.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
      type('PW-AAAAAA');
      enter();
      expect(error).toHaveBeenCalledWith(expect.stringMatching(/cannot reach the server/i));
    });
  });

  it('reloads straight away when the page bumps the refresh key (after a check-in or check-out)', async () => {
    await create([parked('A-02', 'ABC123', 60)]);
    expect(getBoard).toHaveBeenCalledTimes(1);

    fixture.componentRef.setInput('refreshKey', 1);
    fixture.detectChanges();

    expect(getBoard).toHaveBeenCalledTimes(2);
  });

  it('shows an error with a retry when the lot cannot be loaded', async () => {
    getBoard = vi.fn();
    await create([]);
    getBoard.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { title: 'Lot not found.' } })),
    );
    fixture.componentInstance['load']();
    fixture.detectChanges();

    expect(el.querySelector('[role=alert]')?.textContent).toContain('Lot not found.');
  });
});
