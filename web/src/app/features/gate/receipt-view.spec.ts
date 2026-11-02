import { TestBed } from '@angular/core/testing';
import { Receipt } from '../../shared/models/session.models';
import { ReceiptView } from './receipt-view';

const base: Receipt = {
  id: 'r1',
  number: 'RC-1',
  sessionId: 's1',
  ticketCode: 'PW-ABC123',
  plateNumber: 'ABC123',
  lotName: 'Downtown',
  levelNumber: 2,
  spotCode: 'D2-01',
  checkInAtUtc: '2026-06-01T10:00:00Z',
  checkOutAtUtc: '2026-06-01T12:00:00Z',
  amount: 6,
  paymentMethod: 'Mock',
  issuedAtUtc: '2026-06-01T12:00:00Z',
  breakdown: {
    total: 6,
    duration: '02:00:00',
    freeMinutes: 15,
    hourlyRate: 3,
    dailyCap: 20,
    billedHours: 2,
    daysStarted: 1,
    daysCapped: 0,
    isLostTicket: false,
  },
};

const render = (receipt: Receipt) => {
  const fixture = TestBed.createComponent(ReceiptView);
  fixture.componentRef.setInput('receipt', receipt);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
};

describe('ReceiptView', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [ReceiptView] }));

  it('shows who, where, when and the total', () => {
    const el = render(base);
    expect(el.textContent).toContain('RC-1');
    expect(el.textContent).toContain('ABC123');
    expect(el.textContent).toContain('Downtown, level 2, spot D2-01');
    expect(el.querySelector('.total')?.textContent).toBe('6.00');
    expect(el.textContent).toContain('02:00:00');
  });

  it('explains the calculation in words', () => {
    expect(render(base).querySelector('.how')?.textContent).toMatch(
      /2 started hours at 3\.00 an hour, after 15 free minutes/,
    );
  });

  it('says so when the daily cap applied', () => {
    const el = render({
      ...base,
      breakdown: { ...base.breakdown, billedHours: 10, daysCapped: 1, duration: '10:00:00' },
    });
    expect(el.querySelector('.how')?.textContent).toMatch(/daily cap of 20\.00 applied once/);
  });

  it('says nothing is due within the free period', () => {
    const el = render({
      ...base,
      amount: 0,
      breakdown: { ...base.breakdown, total: 0, billedHours: 0, duration: '00:09:00' },
    });
    expect(el.querySelector('.how')?.textContent).toMatch(
      /free period \(15 minutes\): nothing to pay/,
    );
    expect(el.querySelector('.total')?.textContent).toBe('0.00');
  });

  it('explains a lost-ticket flat fee', () => {
    const el = render({
      ...base,
      amount: 50,
      breakdown: { ...base.breakdown, total: 50, isLostTicket: true, billedHours: 0 },
    });
    expect(el.querySelector('.how')?.textContent).toMatch(/lost ticket: a flat fee/i);
  });

  it('is a labelled region for screen readers', () => {
    expect(render(base).querySelector('article')?.getAttribute('aria-label')).toBe('Receipt RC-1');
  });
});
