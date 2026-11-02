import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuditApi, LotsAdminApi, UsersApi } from './admin.api';
import { PricingApi } from './pricing.api';

describe('admin API clients', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  const expectCall = (url: string, method: string, body?: unknown) => {
    const req = http.expectOne(url);
    expect(req.request.method).toBe(method);
    if (body !== undefined) expect(req.request.body).toEqual(body);
    req.flush({});
  };

  it('lots: create / update / close / delete', () => {
    const api = TestBed.inject(LotsAdminApi);
    const save = { name: 'N', address: 'A', allowMotorcycleOverflow: true };
    api.create(save).subscribe();
    expectCall('/api/v1/lots', 'POST', save);
    api.update('l1', save).subscribe();
    expectCall('/api/v1/lots/l1', 'PUT', save);
    api.setActive('l1', false).subscribe();
    expectCall('/api/v1/lots/l1/active', 'PUT', { isActive: false });
    api.delete('l1').subscribe();
    expectCall('/api/v1/lots/l1', 'DELETE');
  });

  it('levels and spots: the paths and bodies match the API', () => {
    const api = TestBed.inject(LotsAdminApi);
    api.addLevel('l1', 2, 'Roof').subscribe();
    expectCall('/api/v1/lots/l1/levels', 'POST', { number: 2, name: 'Roof' });
    api.renameLevel('lv1', 'Top').subscribe();
    expectCall('/api/v1/levels/lv1', 'PUT', { name: 'Top' });
    api.deleteLevel('lv1').subscribe();
    expectCall('/api/v1/levels/lv1', 'DELETE');
    api.addSpot('lv1', { code: 'A-01', type: 'EV' }).subscribe();
    expectCall('/api/v1/levels/lv1/spots', 'POST', { code: 'A-01', type: 'EV' });
    api.updateSpot('s1', { code: 'A-02', type: 'Compact' }).subscribe();
    expectCall('/api/v1/spots/s1', 'PUT', { code: 'A-02', type: 'Compact' });
    api.setSpotService('s1', false).subscribe();
    expectCall('/api/v1/spots/s1/service', 'PUT', { inService: false });
    api.deleteSpot('s1').subscribe();
    expectCall('/api/v1/spots/s1', 'DELETE');
  });

  it('lots list omits an empty search', () => {
    TestBed.inject(LotsAdminApi).list('', 1, 10).subscribe();
    const req = http.expectOne((r) => r.url === '/api/v1/lots');
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    req.flush({});
  });

  it('users: list filters, create, activate/deactivate', () => {
    const api = TestBed.inject(UsersApi);
    api.list({ search: 'sam', role: '', sort: 'name', page: 1, pageSize: 10 }).subscribe();
    const req = http.expectOne((r) => r.url === '/api/v1/users');
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize', 'search', 'sort']);
    req.flush({});
    const user = {
      email: 'a@b.c',
      password: 'Passw0rd1',
      fullName: 'A',
      role: 'Attendant' as const,
    };
    api.create(user).subscribe();
    expectCall('/api/v1/users', 'POST', user);
    api.setActive('u1', false).subscribe();
    expectCall('/api/v1/users/u1/active', 'PUT', { isActive: false });
  });

  it('audit: sends only the filters that are set', () => {
    TestBed.inject(AuditApi)
      .list({
        entityType: 'Spot',
        action: '',
        fromUtc: '2026-06-01T00:00:00Z',
        page: 2,
        pageSize: 25,
      })
      .subscribe();
    const req = http.expectOne((r) => r.url === '/api/v1/audit-logs');
    expect(req.request.params.keys().sort()).toEqual(['entityType', 'fromUtc', 'page', 'pageSize']);
    req.flush({});
  });

  it('pricing: a rule change targets the spot type; the lost-ticket fee has its own route', () => {
    const api = TestBed.inject(PricingApi);
    api
      .setRule('EV', { hourlyRate: 4, dailyCap: 30, freeMinutes: 10, effectiveFromUtc: null })
      .subscribe();
    expectCall('/api/v1/pricing/EV', 'PUT', {
      hourlyRate: 4,
      dailyCap: 30,
      freeMinutes: 10,
      effectiveFromUtc: null,
    });
    api.setLostTicketFee(60, '2030-01-01T00:00:00.000Z').subscribe();
    expectCall('/api/v1/pricing/lost-ticket-fee', 'PUT', {
      amount: 60,
      effectiveFromUtc: '2030-01-01T00:00:00.000Z',
    });
  });
});
