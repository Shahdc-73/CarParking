import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateReservationRequest,
  Reservation,
  ReservationAvailability,
  ReservationStatus,
} from '../../shared/models/reservation.models';
import { SpotType } from '../../features/parking/parking.models';
import { API_BASE_URL } from './api.config';
import { PagedResult, cleanParams } from './paging';

export interface ReservationQuery {
  status?: ReservationStatus | '';
  sort?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class ReservationsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/reservations`;

  list(query: ReservationQuery = {}): Observable<PagedResult<Reservation>> {
    return this.http.get<PagedResult<Reservation>>(this.base, {
      params: cleanParams({ ...query }),
    });
  }

  create(request: CreateReservationRequest): Observable<Reservation> {
    return this.http.post<Reservation>(this.base, request);
  }

  /** The record is kept (status becomes Cancelled); only pending reservations can be cancelled. */
  cancel(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  availability(
    lotId: string,
    spotType: SpotType,
    startUtc: string,
    endUtc: string,
  ): Observable<ReservationAvailability> {
    return this.http.get<ReservationAvailability>(`${this.base}/availability`, {
      params: { lotId, spotType, startUtc, endUtc },
    });
  }
}
