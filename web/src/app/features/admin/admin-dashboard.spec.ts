import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { LotsAdminApi } from '../../core/api/admin.api';
import { ReportsApi } from '../../core/api/reports.api';
import { AdminDashboard } from './admin-dashboard';

const occupancy = {
  generatedAtUtc: '2026-06-01T10:00:00Z',
  overall: {
    lotId: '',
    lotName: 'All',
    isActive: true,
    total: 10,
    available: 4,
    occupied: 5,
    reserved: 1,
    outOfService: 0,
    occupancyRate: 0.5,
  },
  lots: [
    {
      lotId: 'l1',
      lotName: 'Central',
      isActive: true,
      total: 10,
      available: 4,
      occupied: 5,
      reserved: 1,
      outOfService: 0,
      occupancyRate: 0.5,
    },
  ],
};
const performance = {
  from: '2026-06-01',
  to: '2026-06-02',
  lotId: null,
  utcOffsetMinutes: 0,
  totalRevenue: 30,
  paidSessions: 3,
  lostTickets: 1,
  checkIns: 4,
  averageStayMinutes: 95,
  longestStayMinutes: null,
  revenueByDay: [
    { date: '2026-06-01', amount: 10, sessions: 1 },
    { date: '2026-06-02', amount: 20, sessions: 2 },
  ],
  peakHours: Array.from({ length: 24 }, (_, hour) => ({ hour, checkIns: hour === 9 ? 3 : 0 })),
};

function setup(overrides: Partial<Record<'occupancy' | 'performance', unknown>> = {}) {
  const api = {
    occupancy: vi.fn().mockReturnValue(overrides.occupancy ?? of(occupancy)),
    performance: vi.fn().mockReturnValue(overrides.performance ?? of(performance)),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: ReportsApi, useValue: api },
      {
        provide: LotsAdminApi,
        useValue: { list: () => of({ items: [], page: 1, pageSize: 100, totalCount: 0 }) },
      },
    ],
  });
  const fixture = TestBed.createComponent(AdminDashboard);
  fixture.detectChanges();
  return { api, el: fixture.nativeElement as HTMLElement };
}

describe('AdminDashboard', () => {
  it('shows occupancy, the totals and readable durations', () => {
    const { el } = setup();
    const text = el.textContent!;
    expect(text).toContain('50%');
    expect(text).toContain('Central');
    expect(text).toContain('30.00');
    expect(text).toContain('1 h 35 min');
    expect(el.querySelectorAll('app-bar-chart').length).toBe(2);
  });

  it('gives every chart a table alternative', () => {
    const { el } = setup();
    expect(el.querySelectorAll('app-bar-chart table').length).toBe(2);
    expect(el.querySelectorAll('app-bar-chart tbody tr').length).toBe(2 + 24);
  });

  it('asks the API for the default last 7 days of all lots', () => {
    const { api } = setup();
    const [from, to, lot] = api.performance.mock.calls[0];
    expect((Date.parse(to) - Date.parse(from)) / 86_400_000).toBe(6);
    expect(lot).toBe('');
  });

  it('shows the server message when a report fails, without crashing', () => {
    const { el } = setup({ occupancy: throwError(() => ({ status: 500 })) });
    expect(el.querySelector('[role="alert"]')).not.toBeNull();
  });
});
