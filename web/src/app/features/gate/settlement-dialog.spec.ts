import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PricingApi } from '../../core/api/pricing.api';
import { SessionsApi } from '../../core/api/sessions.api';
import { FeePreview, Receipt, SettlementResult } from '../../shared/models/session.models';
import { SettlementData, SettlementDialog } from './settlement-dialog';

const breakdown = {
  total: 6,
  duration: '01:45:00',
  freeMinutes: 15,
  hourlyRate: 3,
  dailyCap: 20,
  billedHours: 2,
  daysStarted: 1,
  daysCapped: 0,
  isLostTicket: false,
};
const preview: FeePreview = {
  sessionId: 's1',
  atUtc: '2026-06-01T12:00:00Z',
  amount: 6,
  breakdown,
};
const receipt: Receipt = {
  id: 'r1',
  number: 'RC-20260601-ABC123',
  sessionId: 's1',
  ticketCode: 'PW-K7M2QX',
  plateNumber: 'ABC123',
  lotName: 'Downtown',
  levelNumber: 1,
  spotCode: 'D1-07',
  checkInAtUtc: '2026-06-01T10:15:00Z',
  checkOutAtUtc: '2026-06-01T12:00:00Z',
  amount: 6,
  paymentMethod: 'Mock',
  issuedAtUtc: '2026-06-01T12:00:00Z',
  breakdown,
};

describe('SettlementDialog', () => {
  let feePreview: ReturnType<typeof vi.fn<(id: string) => Observable<FeePreview>>>;
  let checkOut: ReturnType<typeof vi.fn<(id: string, key: string) => Observable<SettlementResult>>>;
  let lostTicket: ReturnType<typeof vi.fn<(plate: string) => Observable<SettlementResult>>>;
  let current: ReturnType<typeof vi.fn<() => Observable<unknown>>>;
  let close: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<SettlementDialog>;
  let el: HTMLElement;

  const create = async (data: SettlementData) => {
    feePreview = vi.fn().mockReturnValue(of(preview));
    checkOut = vi.fn().mockReturnValue(of({ receipt, replayed: false }));
    lostTicket = vi
      .fn()
      .mockReturnValue(of({ receipt: { ...receipt, amount: 50 }, replayed: false }));
    current = vi
      .fn()
      .mockReturnValue(
        of({ rules: [], lostTicketFee: { amount: 50, effectiveFromUtc: '2025-01-01T00:00:00Z' } }),
      );
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [SettlementDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
        { provide: SessionsApi, useValue: { feePreview, checkOut, lostTicket } },
        { provide: PricingApi, useValue: { current } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(SettlementDialog);
    el = fixture.nativeElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const checkOutData: SettlementData = {
    mode: 'check-out',
    sessionId: 's1',
    plateNumber: 'ABC123',
    spotCode: 'D1-07',
  };
  const lostData: SettlementData = {
    mode: 'lost-ticket',
    plateNumber: 'ABC123',
    spotCode: 'D1-07',
  };

  const click = async (label: RegExp) => {
    [...el.querySelectorAll('button')].find((b) => label.test(b.textContent!))!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  describe('check-out', () => {
    it('shows the amount to pay and how it was reached before anything is charged', async () => {
      await create(checkOutData);

      expect(feePreview).toHaveBeenCalledWith('s1');
      expect(checkOut).not.toHaveBeenCalled();
      expect(el.textContent).toContain('Check out: ABC123');
      expect(el.querySelector('.amount strong')?.textContent).toContain('6.00');
      expect(el.textContent).toContain('01:45:00');
      expect(el.textContent).toContain('2 started hours');
    });

    it('takes the payment with an idempotency key and then shows the receipt', async () => {
      await create(checkOutData);
      await click(/take payment/i);

      expect(checkOut).toHaveBeenCalledTimes(1);
      expect(checkOut.mock.calls[0][0]).toBe('s1');
      expect(checkOut.mock.calls[0][1].length).toBeGreaterThan(8);
      expect(el.querySelector('app-receipt-view')).not.toBeNull();
      expect(el.textContent).toContain('Checked out');
      expect(el.textContent).toContain('RC-20260601-ABC123');
    });

    it('re-sends the SAME key when the user retries after a failure, so the server can never charge twice', async () => {
      await create(checkOutData);
      checkOut
        .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
        .mockReturnValue(of({ receipt, replayed: false }));

      await click(/take payment/i);
      expect(el.querySelector('[role=alert]')?.textContent).toMatch(/cannot reach the server/i);
      expect(el.querySelector('.amount')).not.toBeNull(); // back on the review screen

      await click(/take payment/i);
      expect(checkOut).toHaveBeenCalledTimes(2);
      expect(checkOut.mock.calls[1][1]).toBe(checkOut.mock.calls[0][1]);
      expect(el.querySelector('app-receipt-view')).not.toBeNull();
    });

    it('ignores a second click while the payment is in flight', async () => {
      await create(checkOutData);
      checkOut.mockReturnValue(new Subject<SettlementResult>());

      await click(/take payment/i);
      const pay = [...el.querySelectorAll('button')].find(
        (b) => b.getAttribute('aria-busy') === 'true',
      )!;
      expect(pay.disabled).toBe(true);
      fixture.componentInstance['pay']();
      expect(checkOut).toHaveBeenCalledTimes(1);
    });

    it('explains when the vehicle had already been checked out (a replay)', async () => {
      await create(checkOutData);
      checkOut.mockReturnValue(of({ receipt, replayed: true }));
      await click(/take payment/i);

      expect(el.querySelector('.note')?.textContent).toMatch(
        /already checked out.*nothing was charged again/i,
      );
    });

    it('shows an error with a retry if the fee cannot be calculated', async () => {
      feePreview = vi.fn();
      await create(checkOutData);
      feePreview.mockReturnValue(
        throwError(
          () => new HttpErrorResponse({ status: 409, error: { title: 'No price is configured.' } }),
        ),
      );
      fixture.componentInstance['load']();
      fixture.detectChanges();

      expect(el.querySelector('[role=alert]')?.textContent).toContain('No price is configured.');
    });

    it('closes with true only if something was settled', async () => {
      await create(checkOutData);
      await click(/cancel/i);
      expect(close).toHaveBeenLastCalledWith(false);

      await click(/take payment/i);
      await click(/done/i);
      expect(close).toHaveBeenLastCalledWith(true);
    });
  });

  describe('lost ticket', () => {
    it('shows the flat fee and charges it by plate', async () => {
      await create(lostData);

      expect(feePreview).not.toHaveBeenCalled();
      expect(el.textContent).toContain('flat fee');
      expect(el.querySelector('.amount strong')?.textContent).toContain('50.00');

      await click(/take payment/i);
      expect(lostTicket).toHaveBeenCalledWith('ABC123');
      expect(el.textContent).toContain('Lost ticket settled');
    });

    it('cannot be confirmed when no lost-ticket fee has been set up', async () => {
      current = vi.fn();
      await create(lostData);
      current.mockReturnValue(of({ rules: [], lostTicketFee: null }));
      fixture.componentInstance['load']();
      fixture.detectChanges();

      const pay = [...el.querySelectorAll('button')].find((b) =>
        /take payment/i.test(b.textContent!),
      )!;
      expect(pay.disabled).toBe(true);
      expect(el.textContent).toContain('Not set');
    });
  });
});
