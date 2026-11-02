import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { VehiclesApi } from '../../core/api/vehicles.api';
import { SaveVehicleRequest, Vehicle } from '../../shared/models/vehicle.models';
import { VehicleDialog } from './vehicle-dialog';

const saved: Vehicle = {
  id: 'v1',
  plateNumber: 'ABC123',
  type: 'Car',
  color: 'Blue',
  hasDisabledPermit: false,
  ownerUserId: 'u1',
};

describe('VehicleDialog', () => {
  let create: ReturnType<typeof vi.fn<(r: SaveVehicleRequest) => Observable<Vehicle>>>;
  let update: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<VehicleDialog>;
  let el: HTMLElement;

  const make = async (existing: Vehicle | null) => {
    create = vi.fn().mockReturnValue(of(saved));
    update = vi.fn().mockReturnValue(of({ ...saved, color: 'Red' }));
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [VehicleDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: existing },
        { provide: MatDialogRef, useValue: { close } },
        { provide: VehiclesApi, useValue: { create, update } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(VehicleDialog);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  const fill = (name: string, value: string) => {
    const input = el.querySelector<HTMLInputElement>(`input[formControlName=${name}]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };
  const submit = async () => {
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  it('adds a vehicle and closes with it', async () => {
    await make(null);
    expect(el.querySelector('h2')?.textContent).toContain('Add a vehicle');
    fill('plateNumber', 'abc 123');
    fill('color', 'Blue');
    await submit();

    expect(create).toHaveBeenCalledWith({ plateNumber: 'abc 123', type: 'Car', color: 'Blue' });
    expect(close).toHaveBeenCalledWith(saved);
  });

  it('validates before calling the server', async () => {
    await make(null);
    await submit();
    expect(create).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Enter the plate number.');
    expect(el.textContent).toContain('Enter the colour.');

    fill('plateNumber', '!');
    await submit();
    expect(el.textContent).toContain('2 to 10 letters or digits');
  });

  it('edits type and colour but never the plate', async () => {
    await make(saved);
    expect(el.querySelector('h2')?.textContent).toContain('Edit vehicle');
    const plate = el.querySelector<HTMLInputElement>('input[formControlName=plateNumber]')!;
    expect(plate.disabled).toBe(true);
    expect(plate.value).toBe('ABC123');

    fill('color', 'Red');
    await submit();

    expect(update).toHaveBeenCalledWith('v1', { type: 'Car', color: 'Red' });
    expect(create).not.toHaveBeenCalled();
  });

  it('shows "plate already registered" right where the problem is', async () => {
    await make(null);
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { title: 'Invalid', errors: { plateNumber: ['That plate is taken.'] } },
          }),
      ),
    );
    fill('plateNumber', 'ABC123');
    fill('color', 'Blue');
    await submit();

    expect(el.textContent).toContain('That plate is taken.');
    expect(close).not.toHaveBeenCalled();
  });

  it('shows a general failure (e.g. duplicate plate from the server) in a banner', async () => {
    await make(null);
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {
              title: 'A vehicle with this plate number is already registered.',
              code: 'vehicles.plate_taken',
            },
          }),
      ),
    );
    fill('plateNumber', 'ABC123');
    fill('color', 'Blue');
    await submit();

    expect(el.querySelector('[role=alert]')?.textContent).toContain('already registered');
  });
});
