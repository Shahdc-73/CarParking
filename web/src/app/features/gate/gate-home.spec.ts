import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { SessionsApi } from '../../core/api/sessions.api';
import { Notify } from '../../core/ui/notify';
import { Lot, LotBoard } from '../parking/parking.models';
import { ParkingService } from '../parking/parking.service';
import { GateHome } from './gate-home';
import { ParkedList } from './parked-list';
import { SettlementDialog } from './settlement-dialog';

const lot = (id: string, name: string, isActive = true): Lot => ({
  id,
  name,
  address: 'x',
  isActive,
  allowMotorcycleOverflow: false,
  levelCount: 1,
  spotCount: 10,
});

const emptyBoard = (id: string): LotBoard => ({
  lotId: id,
  lotName: id,
  isActive: true,
  generatedAtUtc: new Date().toISOString(),
  totals: { total: 0, available: 0, occupied: 0, reserved: 0, outOfService: 0 },
  levels: [],
});

describe('GateHome', () => {
  let getLots: ReturnType<typeof vi.fn<() => Observable<Lot[]>>>;
  let getBoard: ReturnType<typeof vi.fn<(id: string) => Observable<LotBoard>>>;
  let open: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<GateHome>;
  let el: HTMLElement;

  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  const create = async (lots: Lot[], dialogResult: boolean | undefined = true) => {
    getLots = vi.fn().mockReturnValue(of(lots));
    getBoard = vi.fn().mockImplementation((id: string) => of(emptyBoard(id)));
    open = vi.fn().mockReturnValue({ afterClosed: () => of(dialogResult) });
    success = vi.fn();
    await TestBed.configureTestingModule({
      imports: [GateHome],
      providers: [
        { provide: ParkingService, useValue: { getLots, getBoard } },
        { provide: SessionsApi, useValue: { checkIn: vi.fn(), list: vi.fn() } },
        { provide: MatDialog, useValue: { open } },
        { provide: Notify, useValue: { success, error: vi.fn() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(GateHome);
    el = fixture.nativeElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const parkedList = () =>
    fixture.debugElement.query((d) => d.componentInstance instanceof ParkedList)
      .componentInstance as ParkedList;

  it('starts on the first open lot and shows the check-in form and the parked list', async () => {
    await create([lot('a', 'Airport'), lot('b', 'Downtown')]);

    expect(el.querySelector('app-check-in-card')).not.toBeNull();
    expect(el.querySelector('app-parked-list')).not.toBeNull();
    expect(getBoard).toHaveBeenCalledWith('a');
  });

  it('offers only OPEN lots', async () => {
    await create([lot('a', 'Closed Lot', false), lot('b', 'Open Lot')]);

    expect(getBoard).toHaveBeenCalledWith('b');
    expect(getBoard).not.toHaveBeenCalledWith('a');
  });

  it('resumes the lot this attendant staffed last time', async () => {
    localStorage.setItem('pw.gate.lot', 'b');
    await create([lot('a', 'Airport'), lot('b', 'Downtown')]);
    expect(getBoard).toHaveBeenCalledWith('b');
  });

  it('ignores a remembered lot that is no longer open', async () => {
    localStorage.setItem('pw.gate.lot', 'gone');
    await create([lot('a', 'Airport')]);
    expect(getBoard).toHaveBeenCalledWith('a');
  });

  it('remembers the chosen lot for next time', async () => {
    await create([lot('a', 'Airport'), lot('b', 'Downtown')]);
    fixture.componentInstance['lotId'].set('b');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(localStorage.getItem('pw.gate.lot')).toBe('b');
  });

  it('still works when browser storage is blocked', async () => {
    const blocked = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const blockedSet = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    await create([lot('a', 'Airport')]);

    expect(getBoard).toHaveBeenCalledWith('a');
    blocked.mockRestore();
    blockedSet.mockRestore();
  });

  it('shows an empty state when no lot is open', async () => {
    await create([lot('a', 'Closed', false)]);
    expect(el.textContent).toContain('No open lots');
    expect(el.querySelector('app-check-in-card')).toBeNull();
  });

  it('shows an error with a retry when lots cannot be loaded', async () => {
    getLots = vi.fn();
    await create([]);
    getLots.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    fixture.componentInstance['loadLots']();
    fixture.detectChanges();

    expect(el.querySelector('[role=alert]')?.textContent).toMatch(/cannot reach the server/i);
  });

  it('opens the check-out dialog for a car and refreshes the list after it settles', async () => {
    await create([lot('a', 'Airport')], true);
    const key = fixture.componentInstance['refreshKey']();

    parkedList().checkOut.emit({
      sessionId: 's1',
      plateNumber: 'ABC123',
      spotCode: 'A-01',
      occupancy: {} as never,
    });

    expect(open).toHaveBeenCalledWith(
      SettlementDialog,
      expect.objectContaining({
        data: { mode: 'check-out', sessionId: 's1', plateNumber: 'ABC123', spotCode: 'A-01' },
      }),
    );
    expect(fixture.componentInstance['refreshKey']()).toBe(key + 1);
    expect(success).toHaveBeenCalledWith('ABC123 checked out.');
  });

  it('opens the lost-ticket dialog', async () => {
    await create([lot('a', 'Airport')], true);
    parkedList().lostTicket.emit({
      sessionId: 's1',
      plateNumber: 'ABC123',
      spotCode: 'A-01',
      occupancy: {} as never,
    });

    expect(open).toHaveBeenCalledWith(
      SettlementDialog,
      expect.objectContaining({
        data: { mode: 'lost-ticket', plateNumber: 'ABC123', spotCode: 'A-01' },
      }),
    );
    expect(success).toHaveBeenCalledWith('Lost ticket closed for ABC123.');
  });

  it('does nothing more when the dialog is dismissed without settling', async () => {
    await create([lot('a', 'Airport')], false);
    const key = fixture.componentInstance['refreshKey']();

    parkedList().checkOut.emit({
      sessionId: 's1',
      plateNumber: 'ABC123',
      spotCode: 'A-01',
      occupancy: {} as never,
    });

    expect(fixture.componentInstance['refreshKey']()).toBe(key);
    expect(success).not.toHaveBeenCalled();
  });
});
