import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import {
  FormGroupDirective,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { toApiError } from '../../core/api/problem-details';
import { SessionsApi } from '../../core/api/sessions.api';
import { DirtyOrSubmittedErrorMatcher, plateNumber } from '../../shared/forms';
import { Session } from '../../shared/models/session.models';
import { VEHICLE_TYPES, VehicleType } from '../../shared/models/vehicle.models';
import { ErrorBanner } from '../../shared/ui/status';

@Component({
  selector: 'app-check-in-card',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    ErrorBanner,
  ],
  templateUrl: './check-in-card.html',
  styleUrl: './check-in-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CheckInCard {
  private readonly sessions = inject(SessionsApi);

  readonly lotId = input.required<string>();
  readonly checkedIn = output<Session>();

  protected readonly form = inject(NonNullableFormBuilder).group({
    plateNumber: ['', [Validators.required, plateNumber]],
    vehicleType: ['' as VehicleType | ''],
  });
  protected readonly types = VEHICLE_TYPES;
  protected readonly errorMatcher = new DirtyOrSubmittedErrorMatcher();

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  /** A plate the system has never seen needs a vehicle type; the server tells us (vehicles.type_required) and we reveal the field. */
  protected readonly needsType = signal(false);
  protected readonly last = signal<Session | null>(null);

  private readonly plateInput = viewChild<ElementRef<HTMLInputElement>>('plate');
  private readonly formDirective = viewChild.required(FormGroupDirective);

  protected submit(): void {
    this.error.set(null);
    this.last.set(null);
    const { plateNumber: plate, vehicleType } = this.form.getRawValue();

    if (this.needsType() && !vehicleType) {
      this.form.controls.vehicleType.markAsTouched();
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.sessions
      .checkIn({ lotId: this.lotId(), plateNumber: plate, vehicleType: vehicleType || null })
      .subscribe({
        next: (session) => {
          this.submitting.set(false);
          this.last.set(session);
          this.needsType.set(false);
          // resetForm (not form.reset): it also clears the directive's "submitted" flag, which is what makes Material paint empty fields red.
          this.formDirective().resetForm({ plateNumber: '', vehicleType: '' });
          this.checkedIn.emit(session);
          this.plateInput()?.nativeElement.focus(); // ready for the next car straight away
        },
        error: (e: unknown) => {
          this.submitting.set(false);
          const apiError = toApiError(e);
          if (apiError.code === 'vehicles.type_required') this.needsType.set(true);
          else this.error.set(apiError.message);
        },
      });
  }
}
