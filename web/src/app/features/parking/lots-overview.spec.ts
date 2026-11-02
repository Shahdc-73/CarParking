import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
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
      providers: [{ provide: ParkingService, useValue: { getOverview } }],
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
});
