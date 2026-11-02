import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, defer, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { LotBoardPage } from './lot-board';
import { BoardSpot, LotBoard, SpotOccupancy, SpotStatus } from './parking.models';
import { ParkingService } from './parking.service';

const T0 = new Date('2026-06-01T10:00:00Z');

const occupancy = (minutesAgo: number, extra: Partial<SpotOccupancy> = {}): SpotOccupancy => ({
  sessionId: 's-' + minutesAgo,
  checkInAtUtc: new Date(T0.getTime() - minutesAgo * 60_000).toISOString(),
  plateNumber: 'ABC1234',
  feeSoFar: 0,
  freeMinutes: 15,
  isMine: false,
  ...extra,
});

const spot = (
  code: string,
  status: SpotStatus,
  o: SpotOccupancy | null = null,
  hold: string | null = null,
): BoardSpot => ({
  id: code,
  code,
  type: 'Standard',
  status,
  occupancy: o,
  holdExpiresAtUtc: hold,
});

const board = (spots: BoardSpot[], generatedAtUtc: string, isActive = true): LotBoard => ({
  lotId: 'lot-1',
  lotName: 'Downtown Garage',
  isActive,
  generatedAtUtc,
  totals: {
    total: spots.length,
    available: spots.filter((s) => s.status === 'Available').length,
    occupied: spots.filter((s) => s.status === 'Occupied').length,
    reserved: spots.filter((s) => s.status === 'Reserved').length,
    outOfService: spots.filter((s) => s.status === 'OutOfService').length,
  },
  levels: [{ id: 'l1', number: 1, name: 'Ground', spots }],
});

/** A fake server whose clock moves with the fake browser clock (plus a constant skew), like a real server's would. */
const serve = (spots: BoardSpot[], skewMs = 0, isActive = true) =>
  defer(() => of(board(spots, new Date(Date.now() + skewMs).toISOString(), isActive)));

describe('LotBoardPage', () => {
  let getBoard: ReturnType<typeof vi.fn<(id: string) => Observable<LotBoard>>>;
  let fixture: ComponentFixture<LotBoardPage>;
  let el: HTMLElement;

  const create = async (source: Observable<LotBoard>) => {
    getBoard = vi.fn().mockReturnValue(source);
    await TestBed.configureTestingModule({
      imports: [LotBoardPage],
      providers: [provideRouter([]), { provide: ParkingService, useValue: { getBoard } }],
    }).compileComponents();
    fixture = TestBed.createComponent(LotBoardPage);
    fixture.componentRef.setInput('id', 'lot-1');
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  const tile = (code: string) =>
    [...el.querySelectorAll<HTMLElement>('.spot')].find(
      (t) => t.querySelector('.code')?.textContent === code,
    )!;
  const clock = (code: string) => tile(code).querySelector('.clock')?.textContent;
  const tick = (ms: number) => {
    vi.advanceTimersByTime(ms);
    fixture.detectChanges();
  };

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(T0);
  });

  afterEach(() => {
    fixture?.destroy();
    vi.restoreAllMocks(); // e.g. the document.hidden spy must not leak into the next test
    vi.useRealTimers();
  });

  it('colours each spot by status and always pairs the colour with a word and an icon', async () => {
    await create(
      serve([
        spot('A-01', 'Available'),
        spot('A-02', 'Occupied', occupancy(5)),
        spot('A-03', 'Reserved'),
        spot('A-04', 'OutOfService'),
      ]),
    );

    expect(tile('A-01').dataset['status']).toBe('Available');
    expect(tile('A-01').textContent).toContain('Free');
    expect(tile('A-02').dataset['status']).toBe('Occupied');
    expect(tile('A-02').textContent).toContain('Occupied');
    expect(tile('A-03').textContent).toContain('Reserved');
    expect(tile('A-04').textContent).toContain('Out of service');
    for (const icon of el.querySelectorAll('.spot .state mat-icon'))
      expect(icon.getAttribute('aria-hidden')).toBe('true');
  });

  it('summarises the counts in a legend, leaving out statuses that do not occur', async () => {
    await create(
      serve([
        spot('A-01', 'Available'),
        spot('A-02', 'Available'),
        spot('A-03', 'Occupied', occupancy(5)),
      ]),
    );

    const legend = [...el.querySelectorAll('.legend-item')].map((i) =>
      i.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(legend).toEqual([
      expect.stringContaining('2 free'),
      expect.stringContaining('1 occupied'),
    ]);
  });

  it('shows a ticking timer on occupied spots, counting from the check-in time', async () => {
    await create(serve([spot('A-01', 'Occupied', occupancy(65))]));
    expect(clock('A-01')).toBe('01:05:00');

    tick(1000);
    expect(clock('A-01')).toBe('01:05:01');
    tick(59_000);
    expect(clock('A-01')).toBe('01:06:00');
  });

  it('has no timer on free spots', async () => {
    await create(serve([spot('A-01', 'Available')]));
    expect(tile('A-01').querySelector('.timer')).toBeNull();
  });

  it('counts down the free period first, then switches to the fee so far', async () => {
    await create(serve([spot('A-01', 'Occupied', occupancy(9, { feeSoFar: 0 }))]));
    expect(tile('A-01').querySelector('.fee')?.textContent).toContain('Free for 00:06:00 more');

    tick(6 * 60_000);
    expect(tile('A-01').querySelector('.fee')?.textContent).not.toContain('Free for');
  });

  it('shows the fee with two decimals, and no fee line when no price is configured', async () => {
    await create(
      serve([
        spot('A-01', 'Occupied', occupancy(120, { feeSoFar: 6 })),
        spot('A-02', 'Occupied', occupancy(120, { feeSoFar: null, freeMinutes: null })),
      ]),
    );

    expect(tile('A-01').querySelector('.fee')?.textContent).toContain('6.00');
    expect(tile('A-02').querySelector('.fee')).toBeNull();
  });

  it('shows the plate for staff and "Your car" for a driver\'s own vehicle', async () => {
    await create(
      serve([
        spot('A-01', 'Occupied', occupancy(30, { isMine: true })),
        spot('A-02', 'Occupied', occupancy(30, { plateNumber: 'ZZZ999' })),
      ]),
    );

    expect(tile('A-01').querySelector('.mine')?.textContent).toContain('Your car');
    expect(tile('A-02').querySelector('.mine')).toBeNull();
    expect(tile('A-02').querySelector('.plate')?.textContent).toBe('ZZZ999');
  });

  it("shows another driver's occupied spot as red and nothing else (no timer, plate or fee)", async () => {
    await create(serve([spot('A-01', 'Occupied', null)]));

    expect(tile('A-01').dataset['status']).toBe('Occupied');
    expect(tile('A-01').querySelector('.timer')).toBeNull();
    expect(tile('A-01').querySelector('.plate')).toBeNull();
    expect(tile('A-01').querySelector('.fee')).toBeNull();
  });

  it('uses the SERVER clock, so a PC with a wrong clock still shows the right stay', async () => {
    // This browser's clock is 4 minutes AHEAD of the server's. The car checked in 65 minutes before the server's "now".
    vi.setSystemTime(new Date(T0.getTime() + 4 * 60_000));
    await create(serve([spot('A-01', 'Occupied', occupancy(65))], -4 * 60_000));

    expect(clock('A-01')).toBe('01:05:00'); // without the correction this would read 01:09:00
    tick(1000);
    expect(clock('A-01')).toBe('01:05:01');
  });

  it('shows when a reservation hold ends', async () => {
    await create(serve([spot('A-01', 'Reserved', null, '2026-06-01T10:15:00Z')]));
    expect(tile('A-01').querySelector('.fee')?.textContent).toMatch(/Held until/);
  });

  it('describes each spot to screen readers with a stable sentence, not a ticking clock', async () => {
    await create(
      serve([spot('A-01', 'Occupied', occupancy(125, { feeSoFar: 9 })), spot('A-02', 'Available')]),
    );

    expect(tile('A-01').getAttribute('aria-label')).toBe(
      'Spot A-01, Standard, occupied, parked for 2 hours 5 minutes, fee so far 9.00',
    );
    expect(tile('A-02').getAttribute('aria-label')).toBe('Spot A-02, Standard, free');
    expect(tile('A-01').querySelector('.timer')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('refreshes itself every 30 seconds so occupancy and fees stay current', async () => {
    await create(serve([spot('A-01', 'Available')]));
    expect(getBoard).toHaveBeenCalledTimes(1);

    tick(30_000);
    expect(getBoard).toHaveBeenCalledTimes(2);
    tick(30_000);
    expect(getBoard).toHaveBeenCalledTimes(3);
  });

  it('does not refresh while the browser tab is hidden', async () => {
    await create(serve([spot('A-01', 'Available')]));
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);

    tick(90_000);
    expect(getBoard).toHaveBeenCalledTimes(1);
  });

  it('stops all timers when the page is left', async () => {
    await create(serve([spot('A-01', 'Available')]));
    fixture.destroy();

    vi.advanceTimersByTime(120_000);
    expect(getBoard).toHaveBeenCalledTimes(1);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('keeps showing the last good data when a background refresh fails, and says so', async () => {
    await create(serve([spot('A-01', 'Occupied', occupancy(5))]));
    getBoard.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

    tick(30_000);

    expect(tile('A-01')).toBeTruthy(); // not replaced by an error page
    expect(el.querySelector('[role=alert]')?.textContent).toMatch(/could not refresh.*last data/i);
  });

  it('shows an error with a retry when the first load fails', async () => {
    await create(
      throwError(() => new HttpErrorResponse({ status: 404, error: { title: 'Lot not found.' } })),
    );

    expect(el.querySelector('[role=alert]')?.textContent).toContain('Lot not found.');
    getBoard.mockReturnValue(serve([spot('A-01', 'Available')]));
    el.querySelector<HTMLButtonElement>('[role=alert] button')!.click();
    fixture.detectChanges();

    expect(el.querySelector('[role=alert]')).toBeNull();
    expect(tile('A-01')).toBeTruthy();
  });

  it('shows an empty state for a lot without spots and a note for a closed lot', async () => {
    await create(serve([], 0, false));

    expect(el.textContent).toContain('No spots in this lot yet');
    expect(el.textContent).toContain('This lot is closed.');
  });

  it('loads the lot named in the route and links back to the overview', async () => {
    await create(serve([spot('A-01', 'Available')]));

    expect(getBoard).toHaveBeenCalledWith('lot-1');
    expect(el.querySelector('a.back')?.getAttribute('href')).toBe('/lots');
  });
});
