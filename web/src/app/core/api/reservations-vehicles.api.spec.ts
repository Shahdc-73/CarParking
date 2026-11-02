import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ReservationsApi } from './reservations.api';
import { VehiclesApi } from './vehicles.api';

describe('ReservationsApi and VehiclesApi', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lists reservations with only the filters that are set', () => {
    TestBed.inject(ReservationsApi)
      .list({ status: '', sort: 'start_desc', page: 2, pageSize: 10 })
      .subscribe();
    const req = http.expectOne((r) => r.url === '/api/v1/reservations');
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize', 'sort']);
    req.flush({});
  });

  it('creates a reservation with the UTC window', () => {
    const body = {
      lotId: 'l',
      vehicleId: 'v',
      spotType: 'EV' as const,
      startUtc: '2026-06-01T10:00:00.000Z',
      endUtc: '2026-06-01T12:00:00.000Z',
    };
    TestBed.inject(ReservationsApi).create(body).subscribe();
    const req = http.expectOne('/api/v1/reservations');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('cancels with DELETE', () => {
    TestBed.inject(ReservationsApi).cancel('r1').subscribe();
    expect(http.expectOne('/api/v1/reservations/r1').request.method).toBe('DELETE');
  });

  it('asks for availability with the lot, type and window', () => {
    TestBed.inject(ReservationsApi).availability('l1', 'EV', 'a', 'b').subscribe();
    const req = http.expectOne((r) => r.url === '/api/v1/reservations/availability');
    expect(
      Object.fromEntries(req.request.params.keys().map((k) => [k, req.request.params.get(k)])),
    ).toEqual({ lotId: 'l1', spotType: 'EV', startUtc: 'a', endUtc: 'b' });
    req.flush({});
  });

  it('updates a vehicle without sending the plate (it cannot change)', () => {
    TestBed.inject(VehiclesApi).update('v1', { type: 'Car', color: 'Red' }).subscribe();
    const req = http.expectOne('/api/v1/vehicles/v1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ type: 'Car', color: 'Red' });
    req.flush({});
  });
});
