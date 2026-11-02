import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { LotOverview } from './parking.models';
import { ParkingService } from './parking.service';
import { LotsOverview } from './lots-overview';

const overview = (
  name: string,
  available: number,
  total: number,
  isActive = true,
): LotOverview => ({
  lot: {
    id: name,
    name,
    address: '1 Main St',
    isActive,
    allowMotorcycleOverflow: false,
    levelCount: 1,
    spotCount: total,
  },
  availability: {
    lotId: name,
    lotName: name,
    totals: { total, available, occupied: total - available, reserved: 0, outOfService: 0 },
    byType: [
      {
        type: 'Standard',
        counts: { total, available, occupied: total - available, reserved: 0, outOfService: 0 },
      },
    ],
  },
});

describe('LotsOverview', () => {
  let getOverview: ReturnType<typeof vi.fn<() => Observable<LotOverview[]>>>;
  let fixture: ComponentFixture<LotsOverview>;
  let el: HTMLElement;

  const create = async (source: Observable<LotOverview[]>) => {
    getOverview = vi.fn().mockReturnValue(source);
    await TestBed.configureTestingModule({
      imports: [LotsOverview],
      providers: [provideRouter([]), { provide: ParkingService, useValue: { getOverview } }],
    }).compileComponents();
    fixture = TestBed.createComponent(LotsOverview);
    el = fixture.nativeElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  it('shows a loading indicator until the data arrives', async () => {
    await create(new Subject<LotOverview[]>());
    expect(el.querySelector('mat-progress-bar[aria-label="Loading parking lots"]')).not.toBeNull();
    expect(el.querySelector('.grid')).toBeNull();
  });

  it('shows each lot with free spots, total and a labelled fullness meter', async () => {
    await create(of([overview('Downtown Garage', 12, 32), overview('Airport', 28, 28)]));

    const cards = el.querySelectorAll('.grid li');
    expect(cards.length).toBe(2);
    expect(cards[0].textContent).toContain('Downtown Garage');
    expect(cards[0].textContent).toContain('12');
    expect(cards[0].textContent).toContain('free of 32');
    expect(cards[0].querySelector('mat-progress-bar')?.getAttribute('aria-label')).toBe(
      'Downtown Garage is 63 percent full',
    );
    expect(cards[1].querySelector('mat-progress-bar')?.getAttribute('aria-label')).toBe(
      'Airport is 0 percent full',
    );
  });

  it('presents the per-type numbers as a real table with a caption for screen readers', async () => {
    await create(of([overview('Downtown Garage', 12, 32)]));
    const table = el.querySelector('table')!;
    expect(table.querySelector('caption')?.textContent).toContain('Downtown Garage');
    expect(table.querySelectorAll('th[scope=col]').length).toBe(3);
    expect(table.querySelector('tbody th[scope=row]')?.textContent).toContain('Standard');
  });

  it('marks closed lots', async () => {
    await create(of([overview('Old Lot', 0, 10, false)]));
    expect(el.textContent).toContain('Closed');
  });

  it('shows an empty state when there are no lots', async () => {
    await create(of([]));
    expect(el.textContent).toContain('No parking lots yet');
  });

  it('shows a friendly alert on failure and retries when asked', async () => {
    await create(throwError(() => new HttpErrorResponse({ status: 0 })));
    expect(el.querySelector('[role=alert]')?.textContent).toMatch(/cannot reach the server/i);

    getOverview.mockReturnValue(of([overview('Airport', 5, 10)]));
    el.querySelector<HTMLButtonElement>('[role=alert] button')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[role=alert]')).toBeNull();
    expect(el.textContent).toContain('Airport');
    expect(getOverview).toHaveBeenCalledTimes(2);
  });

  const stat = (index: number) => {
    const node = el.querySelectorAll('.stat')[index];
    return {
      value: node.querySelector('strong')?.textContent,
      label: node.querySelector('span')?.textContent,
    };
  };

  it('summarises only the OPEN lots at the top', async () => {
    await create(
      of([overview('A', 10, 20), overview('B', 5, 10), overview('Closed', 99, 99, false)]),
    );

    expect(stat(0)).toEqual({ value: '15', label: 'free spots now' });
    expect(stat(1)).toEqual({ value: '2', label: 'lots open' });
    expect(stat(2)).toEqual({ value: '30', label: 'spots in total' });
  });

  it('uses the singular for a single open lot', async () => {
    await create(of([overview('Only', 1, 2)]));
    expect(stat(1)).toEqual({ value: '1', label: 'lot open' });
  });

  it('labels each lot with a status chip that has text, not just colour', async () => {
    await create(
      of([overview('Quiet', 9, 10), overview('Packed', 1, 10), overview('Full', 0, 10)]),
    );

    const chips = [...el.querySelectorAll('.chip')].map((c) =>
      c.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(chips).toEqual([
      expect.stringContaining('Plenty of space'),
      expect.stringContaining('Almost full'),
      expect.stringContaining('Full'),
    ]);
  });

  it('shows a clear message instead of an empty table for a lot with no spots', async () => {
    await create(of([overview('Brand new', 0, 0)]));

    expect(el.textContent).toContain('No spots have been added to this lot yet.');
    expect(el.querySelector('table')).toBeNull();
    expect(el.querySelector('.chip')?.textContent).toContain('No spots yet');
  });

  it('shows when the data was last updated', async () => {
    await create(of([overview('Airport', 5, 10)]));
    expect(el.querySelector('.updated')?.textContent).toMatch(/^Updated \d{1,2}:\d{2}/);
  });

  it('links each lot to its spot board', async () => {
    await create(of([overview('Downtown Garage', 12, 32)]));
    const link = el.querySelector<HTMLAnchorElement>('a.lot-link')!;
    expect(link.getAttribute('href')).toBe('/lots/Downtown%20Garage'); // the test uses the lot name as its id
    expect(link.textContent).toContain('View spots');
  });
});
