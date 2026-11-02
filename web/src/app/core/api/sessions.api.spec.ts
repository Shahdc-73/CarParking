import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessionsApi } from './sessions.api';

describe('SessionsApi', () => {
  let api: SessionsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(SessionsApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  const receipt = { id: 'r1', amount: 6 };

  it('check-out sends the idempotency key header and reports a first-time check-out as not replayed', () => {
    let result: unknown;
    api.checkOut('s1', 'key-123').subscribe((r) => (result = r));

    const req = http.expectOne('/api/v1/sessions/s1/check-out');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('key-123');
    req.flush(receipt, { headers: { 'Idempotent-Replayed': 'false' } });

    expect(result).toEqual({ receipt, replayed: false });
  });

  it('flags a replayed check-out (the server returned the existing receipt, nothing was charged again)', () => {
    let result: { replayed: boolean } | undefined;
    api.checkOut('s1', 'key-123').subscribe((r) => (result = r));
    http
      .expectOne('/api/v1/sessions/s1/check-out')
      .flush(receipt, { headers: { 'Idempotent-Replayed': 'true' } });
    expect(result?.replayed).toBe(true);
  });

  it('lost ticket posts the plate and reads the replay header too', () => {
    let result: { replayed: boolean } | undefined;
    api.lostTicket('ABC123').subscribe((r) => (result = r));
    const req = http.expectOne('/api/v1/sessions/lost-ticket');
    expect(req.request.body).toEqual({ plateNumber: 'ABC123' });
    req.flush(receipt);
    expect(result?.replayed).toBe(false);
  });

  it('list sends only the filters that have a value', () => {
    api.list({ status: 'Active', plate: '', ticketCode: 'PW-ABC123', page: 2 }).subscribe();
    const req = http.expectOne((r) => r.url === '/api/v1/sessions');
    expect(req.request.params.keys().sort()).toEqual(['page', 'status', 'ticketCode']);
    expect(req.request.params.get('ticketCode')).toBe('PW-ABC123');
    req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
  });

  it('check-in posts to the check-in endpoint', () => {
    api.checkIn({ lotId: 'l1', plateNumber: 'AB123', vehicleType: 'Car' }).subscribe();
    const req = http.expectOne('/api/v1/sessions/check-in');
    expect(req.request.body).toEqual({ lotId: 'l1', plateNumber: 'AB123', vehicleType: 'Car' });
    req.flush({});
  });
});
