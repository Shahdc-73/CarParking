import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SaveVehicleRequest, Vehicle } from '../../shared/models/vehicle.models';
import { API_BASE_URL } from './api.config';
import { PagedResult, cleanParams } from './paging';

@Injectable({ providedIn: 'root' })
export class VehiclesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/vehicles`;

  /** Drivers get their own vehicles; staff get everyone's. */
  list(search = '', page = 1, pageSize = 100): Observable<PagedResult<Vehicle>> {
    return this.http.get<PagedResult<Vehicle>>(this.base, {
      params: cleanParams({ search, page, pageSize }),
    });
  }

  create(request: SaveVehicleRequest): Observable<Vehicle> {
    return this.http.post<Vehicle>(this.base, request);
  }

  update(id: string, request: Omit<SaveVehicleRequest, 'plateNumber'>): Observable<Vehicle> {
    return this.http.put<Vehicle>(`${this.base}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
