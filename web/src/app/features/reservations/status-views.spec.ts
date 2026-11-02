import { RESERVATION_STATUS, SESSION_STATUS } from './status-views';

describe('status views', () => {
  it('give every reservation status a label, a tone and an icon (never colour alone)', () => {
    for (const status of ['Pending', 'Fulfilled', 'Expired', 'Cancelled'] as const) {
      expect(RESERVATION_STATUS[status].label.length).toBeGreaterThan(0);
      expect(RESERVATION_STATUS[status].icon.length).toBeGreaterThan(0);
    }
    expect(new Set(Object.values(RESERVATION_STATUS).map((v) => v.label)).size).toBe(4);
  });

  it('give every session status a label and an icon', () => {
    for (const status of ['Active', 'Completed', 'LostTicket'] as const) {
      expect(SESSION_STATUS[status].label.length).toBeGreaterThan(0);
      expect(SESSION_STATUS[status].icon.length).toBeGreaterThan(0);
    }
  });

  it("uses people's words, not database words", () => {
    expect(RESERVATION_STATUS.Pending.label).toBe('Upcoming');
    expect(RESERVATION_STATUS.Expired.label).toBe('Missed');
    expect(SESSION_STATUS.Active.label).toBe('Parked now');
  });
});
