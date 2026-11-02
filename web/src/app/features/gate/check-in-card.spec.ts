import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { SessionsApi } from '../../core/api/sessions.api';
import { CheckInRequest, Session } from '../../shared/models/session.models';
import { CheckInCard } from './check-in-card';

const session: Session = {
  id: 's1',
  ticketCode: 'PW-K7M2QX',
  vehicleId: 'v1',
  plateNumber: 'ABC123',
  vehicleType: 'Car',
  lotId: 'lot-1',
  lotName: 'Downtown',
  levelNumber: 1,
  spotId: 'sp1',
  spotCode: 'D1-07',
  spotType: 'Standard',
  checkInAtUtc: '2026-06-01T10:00:00Z',
  checkOutAtUtc: null,
  status: 'Active',
  receiptId: null,
  receiptNumber: null,
  amountPaid: null,
  reservationId: null,
};

const problem = (status: number, code: string, title: string) =>
  new HttpErrorResponse({ status, error: { title, code } });

describe('CheckInCard', () => {
  let checkIn: ReturnType<typeof vi.fn<(r: CheckInRequest) => Observable<Session>>>;
  let fixture: ComponentFixture<CheckInCard>;
  let el: HTMLElement;
  let emitted: Session[];

  beforeEach(async () => {
    checkIn = vi.fn();
    emitted = [];
    await TestBed.configureTestingModule({
      imports: [CheckInCard],
      providers: [{ provide: SessionsApi, useValue: { checkIn } }],
    }).compileComponents();
    fixture = TestBed.createComponent(CheckInCard);
    fixture.componentRef.setInput('lotId', 'lot-1');
    fixture.componentInstance.checkedIn.subscribe((s) => emitted.push(s));
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  const type = (value: string) => {
    const input = el.querySelector<HTMLInputElement>('input[formControlName=plateNumber]')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };
  const submit = async () => {
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  it('asks for a plate and rejects nonsense before calling the server', async () => {
    await submit();
    expect(checkIn).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Enter the plate number.');

    type('!');
    await submit();
    expect(checkIn).not.toHaveBeenCalled();
    expect(el.textContent).toContain('2 to 10 letters or digits');
  });

  it('checks the car in, shows the ticket big, emits the session and clears the form for the next car', async () => {
    checkIn.mockReturnValue(of(session));
    type('abc 123');
    await submit();

    expect(checkIn).toHaveBeenCalledWith({
      lotId: 'lot-1',
      plateNumber: 'abc 123',
      vehicleType: null,
    });
    expect(el.querySelector('.ticket .code')?.textContent).toBe('PW-K7M2QX');
    expect(el.querySelector('.ticket')?.getAttribute('role')).toBe('status');
    expect(el.textContent).toContain('D1-07');
    expect(emitted).toEqual([session]);
    expect(el.querySelector<HTMLInputElement>('input[formControlName=plateNumber]')!.value).toBe(
      '',
    );
  });

  it('does not nag about the empty plate field just because the attendant clicked away from it', () => {
    const input = el.querySelector<HTMLInputElement>('input[formControlName=plateNumber]')!;
    input.focus();
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(el.querySelector('mat-error')).toBeNull();

    type('!'); // but typing something invalid is reported straight away
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(el.querySelector('mat-error')?.textContent).toContain('2 to 10 letters or digits');
  });

  it('does not paint the emptied plate field red after a successful check-in', async () => {
    checkIn.mockReturnValue(of(session));
    type('ABC123');
    await submit();

    expect(el.querySelector('mat-error')).toBeNull();
    expect(el.querySelector('mat-form-field.mat-form-field-invalid')).toBeNull();
  });

  it('does not show the vehicle type until the server says the plate is new', async () => {
    expect(el.querySelector('mat-select')).toBeNull();

    checkIn.mockReturnValue(
      throwError(() => problem(400, 'vehicles.type_required', 'This plate is not registered.')),
    );
    type('NEW777');
    await submit();

    expect(el.querySelector('mat-select')).not.toBeNull();
    expect(el.textContent).toContain('New plate: choose the vehicle type');
    expect(el.querySelector('[role=alert]')).toBeNull(); // it is a prompt, not an error
  });

  it('after the type is chosen it sends the type with the same plate', async () => {
    checkIn
      .mockReturnValueOnce(throwError(() => problem(400, 'vehicles.type_required', 'new')))
      .mockReturnValue(of(session));
    type('NEW777');
    await submit();

    // submitting again without choosing a type must not call the server
    await submit();
    expect(checkIn).toHaveBeenCalledTimes(1);

    fixture.componentInstance['form'].controls.vehicleType.setValue('Motorcycle');
    await submit();

    expect(checkIn).toHaveBeenLastCalledWith({
      lotId: 'lot-1',
      plateNumber: 'NEW777',
      vehicleType: 'Motorcycle',
    });
    expect(el.querySelector('mat-select')).toBeNull(); // hidden again for the next car
  });

  it.each([
    ['sessions.vehicle_already_parked', 'This vehicle already has an active parking session.'],
    ['sessions.no_spot_available', 'No compatible spot is free in this lot.'],
    ['lots.inactive', 'This lot is closed.'],
  ])("shows the server's message for %s in an alert", async (code, message) => {
    checkIn.mockReturnValue(throwError(() => problem(409, code, message)));
    type('ABC123');
    await submit();

    expect(el.querySelector('[role=alert]')?.textContent).toContain(message);
    expect(emitted).toEqual([]);
  });

  it('disables the button while checking in so a double click cannot check in twice', async () => {
    checkIn.mockReturnValue(new Subject<Session>());
    type('ABC123');
    await submit();

    const button = el.querySelector<HTMLButtonElement>('button[type=submit]')!;
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(checkIn).toHaveBeenCalledTimes(1);
  });

  it("mentions when the driver's reservation was used", async () => {
    checkIn.mockReturnValue(of({ ...session, reservationId: 'res-1' }));
    type('ABC123');
    await submit();
    expect(el.querySelector('.reserved')?.textContent).toContain("driver's reservation");
  });
});
