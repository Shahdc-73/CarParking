import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ReservationsApi } from '../../core/api/reservations.api';
import { VehiclesApi } from '../../core/api/vehicles.api';
import { toLocalInput } from '../../core/time';
import {
  CreateReservationRequest,
  Reservation,
  ReservationAvailability,
} from '../../shared/models/reservation.models';
import { Vehicle } from '../../shared/models/vehicle.models';
import { Lot } from '../parking/parking.models';
import { ParkingService } from '../parking/parking.service';
import { ReservationDialog } from './reservation-dialog';

const lot = (id: string, name: string, isActive = true): Lot => ({
  id,
  name,
  address: 'x',
  isActive,
  allowMotorcycleOverflow: false,
  levelCount: 1,
  spotCount: 5,
});
const car = (id: string, plate: string): Vehicle => ({
  id,
  plateNumber: plate,
  type: 'Car',
  color: 'Blue',
  hasDisabledPermit: false,
  ownerUserId: 'u',
});
const available = (spots: number): ReservationAvailability => ({
  lotId: 'l',
  spotType: 'Standard',
  startUtc: '',
  endUtc: '',
  availableSpots: spots,
});
const created = { id: 'r1', spotCode: 'A-01', lotName: 'Downtown' } as Reservation;

describe('ReservationDialog', () => {
  let availability: ReturnType<
    typeof vi.fn<(...a: unknown[]) => Observable<ReservationAvailability>>
  >;
  let create: ReturnType<typeof vi.fn<(r: CreateReservationRequest) => Observable<Reservation>>>;
  let close: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<ReservationDialog>;
  let el: HTMLElement;

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    fixture?.destroy();
    vi.useRealTimers();
  });

  const make = async (lots: Lot[], vehicles: Vehicle[]) => {
    availability = vi.fn().mockReturnValue(of(available(3)));
    create = vi.fn().mockReturnValue(of(created));
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [ReservationDialog],
      providers: [
        provideRouter([]),
        { provide: MatDialogRef, useValue: { close } },
        { provide: ParkingService, useValue: { getLots: () => of(lots) } },
        {
          provide: VehiclesApi,
          useValue: {
            list: () =>
              of({
                items: vehicles,
                page: 1,
                pageSize: 100,
                totalCount: vehicles.length,
                totalPages: 1,
              }),
          },
        },
        { provide: ReservationsApi, useValue: { availability, create } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ReservationDialog);
    el = fixture.nativeElement;
    fixture.detectChanges();
    vi.advanceTimersByTime(400); // the debounce
    fixture.detectChanges();
  };

  const form = () => fixture.componentInstance['form'];
  const submit = () => {
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    fixture.detectChanges();
  };

  it('preselects the lot and the vehicle when there is only one of each', async () => {
    await make([lot('l1', 'Downtown')], [car('v1', 'ABC123')]);
    expect(form().controls.lotId.value).toBe('l1');
    expect(form().controls.vehicleId.value).toBe('v1');
  });

  it('offers only open lots and leaves the choice to the driver when there are several', async () => {
    await make(
      [lot('l1', 'A'), lot('l2', 'B'), lot('l3', 'Closed', false)],
      [car('v1', 'ABC123'), car('v2', 'XYZ789')],
    );
    expect(fixture.componentInstance['lots']().map((l) => l.id)).toEqual(['l1', 'l2']);
    expect(form().controls.lotId.value).toBe('');
  });

  it('starts with a sensible arrival time: in the future, on a quarter-hour', async () => {
    await make([lot('l1', 'Downtown')], [car('v1', 'ABC123')]);
    const start = new Date(form().controls.start.value);
    expect(start.getTime()).toBeGreaterThan(Date.now());
    expect(start.getMinutes() % 15).toBe(0);
    expect(form().controls.durationMinutes.value).toBe(120);
  });

  it('tells a driver without vehicles to add one first, instead of showing a form that cannot be submitted', async () => {
    await make([lot('l1', 'Downtown')], []);
    expect(el.textContent).toContain('You need a vehicle before you can reserve a spot.');
    expect(el.querySelector('form')).toBeNull();
    expect(el.querySelector('a[href="/my/vehicles"]')).not.toBeNull();
  });

  it('checks availability for the chosen window as the form is filled, converting LOCAL time to UTC', async () => {
    await make([lot('l1', 'Downtown')], [car('v1', 'ABC123')]);
    form().patchValue({ spotType: 'EV', start: '2030-06-01T13:00', durationMinutes: 60 });
    vi.advanceTimersByTime(400);
    fixture.detectChanges();

    const [lotId, spotType, startUtc, endUtc] = availability.mock.calls.at(-1)!;
    expect([lotId, spotType]).toEqual(['l1', 'EV']);
    expect(startUtc).toBe(new Date(2030, 5, 1, 13, 0).toISOString());
    expect(endUtc).toBe(new Date(2030, 5, 1, 14, 0).toISOString());
  });

  it('shows how many spots are free, or that none are', async () => {
    await make([lot('l1', 'Downtown')], [car('v1', 'ABC123')]);
    expect(el.querySelector('.availability')?.textContent).toContain(
      '3 spots are free for that time.',
    );

    availability.mockReturnValue(of(available(1)));
    form().patchValue({ durationMinutes: 240 });
    vi.advanceTimersByTime(400);
    fixture.detectChanges();
    expect(el.querySelector('.availability')?.textContent).toContain('1 spot is free');

    availability.mockReturnValue(of(available(0)));
    form().patchValue({ durationMinutes: 480 });
    vi.advanceTimersByTime(400);
    fixture.detectChanges();
    expect(el.querySelector('.availability')?.textContent).toContain(
      'No spot of this type is free then',
    );
  });

  it('does not hammer the server while the driver is still changing things (debounce)', async () => {
    await make([lot('l1', 'Downtown')], [car('v1', 'ABC123')]);
    const before = availability.mock.calls.length;

    for (const minutes of [30, 60, 120, 240]) {
      form().patchValue({ durationMinutes: minutes });
      vi.advanceTimersByTime(100);
    }
    vi.advanceTimersByTime(400);
    fixture.detectChanges();

    expect(availability.mock.calls.length - before).toBe(1);
  });

  it('rejects an arrival time in the past', async () => {
    await make([lot('l1', 'Downtown')], [car('v1', 'ABC123')]);
    form().controls.start.setValue(toLocalInput(new Date(Date.now() - 3_600_000)));
    expect(form().controls.start.hasError('past')).toBe(true);

    submit();
    expect(create).not.toHaveBeenCalled();
  });

  it('reserves with the UTC start and the computed end, then closes with the reservation', async () => {
    await make([lot('l1', 'Downtown')], [car('v1', 'ABC123')]);
    form().patchValue({ spotType: 'Compact', start: '2030-06-01T09:30', durationMinutes: 240 });
    submit();

    expect(create).toHaveBeenCalledWith({
      lotId: 'l1',
      vehicleId: 'v1',
      spotType: 'Compact',
      startUtc: new Date(2030, 5, 1, 9, 30).toISOString(),
      endUtc: new Date(2030, 5, 1, 13, 30).toISOString(),
    });
    expect(close).toHaveBeenCalledWith(created);
  });

  it("shows the server's reason when it refuses (e.g. an incompatible spot type) and stays open", async () => {
    await make([lot('l1', 'Downtown')], [car('v1', 'ABC123')]);
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {
              title: 'This vehicle cannot use EV spots.',
              code: 'reservations.incompatible_spot_type',
            },
          }),
      ),
    );
    form().patchValue({ spotType: 'EV', start: '2030-06-01T09:30' });
    submit();

    expect(el.querySelector('[role=alert]')?.textContent).toContain('cannot use EV spots');
    expect(close).not.toHaveBeenCalled();
  });

  it('reports an availability check that fails without blocking the form', async () => {
    await make([lot('l1', 'Downtown')], [car('v1', 'ABC123')]);
    availability.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    form().patchValue({ durationMinutes: 60 });
    vi.advanceTimersByTime(400);
    fixture.detectChanges();

    expect(el.querySelector('.availability')?.textContent).toMatch(/cannot reach the server/i);
    expect(el.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
  });
});
