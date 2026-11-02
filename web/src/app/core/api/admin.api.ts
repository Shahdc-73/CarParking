import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Lot } from '../../features/parking/parking.models';
import {
  AdminUser,
  AuditLog,
  CreateUserRequest,
  LotDetail,
  LotLevel,
  SaveLotRequest,
  SaveSpotRequest,
} from '../../shared/models/admin.models';
import { API_BASE_URL } from './api.config';
import { PagedResult, cleanParams } from './paging';

/** Lots, levels and spots (admin writes). Reading availability lives in ParkingService. */
@Injectable({ providedIn: 'root' })
export class LotsAdminApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  list(search = '', page = 1, pageSize = 10): Observable<PagedResult<Lot>> {
    return this.http.get<PagedResult<Lot>>(`${this.base}/lots`, {
      params: cleanParams({ search, page, pageSize }),
    });
  }

  get(id: string): Observable<LotDetail> {
    return this.http.get<LotDetail>(`${this.base}/lots/${id}`);
  }

  create(request: SaveLotRequest): Observable<Lot> {
    return this.http.post<Lot>(`${this.base}/lots`, request);
  }

  update(id: string, request: SaveLotRequest): Observable<Lot> {
    return this.http.put<Lot>(`${this.base}/lots/${id}`, request);
  }

  setActive(id: string, isActive: boolean): Observable<void> {
    return this.http.put<void>(`${this.base}/lots/${id}/active`, { isActive });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/lots/${id}`);
  }

  addLevel(lotId: string, number: number, name: string): Observable<LotLevel> {
    return this.http.post<LotLevel>(`${this.base}/lots/${lotId}/levels`, { number, name });
  }

  renameLevel(levelId: string, name: string): Observable<LotLevel> {
    return this.http.put<LotLevel>(`${this.base}/levels/${levelId}`, { name });
  }

  deleteLevel(levelId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/levels/${levelId}`);
  }

  addSpot(levelId: string, request: SaveSpotRequest): Observable<unknown> {
    return this.http.post(`${this.base}/levels/${levelId}/spots`, request);
  }

  updateSpot(spotId: string, request: SaveSpotRequest): Observable<unknown> {
    return this.http.put(`${this.base}/spots/${spotId}`, request);
  }

  /** `inService=false` takes the spot out of service; true brings it back. */
  setSpotService(spotId: string, inService: boolean): Observable<unknown> {
    return this.http.put(`${this.base}/spots/${spotId}/service`, { inService });
  }

  deleteSpot(spotId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/spots/${spotId}`);
  }
}

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/users`;

  list(query: {
    search?: string;
    role?: string;
    sort?: string;
    page?: number;
    pageSize?: number;
  }): Observable<PagedResult<AdminUser>> {
    return this.http.get<PagedResult<AdminUser>>(this.base, { params: cleanParams({ ...query }) });
  }

  create(request: CreateUserRequest): Observable<AdminUser> {
    return this.http.post<AdminUser>(this.base, request);
  }

  setActive(id: string, isActive: boolean): Observable<void> {
    return this.http.put<void>(`${this.base}/${id}/active`, { isActive });
  }
}

export interface AuditQuery {
  entityType?: string;
  action?: string;
  fromUtc?: string;
  toUtc?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class AuditApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/audit-logs`;

  list(query: AuditQuery): Observable<PagedResult<AuditLog>> {
    return this.http.get<PagedResult<AuditLog>>(this.base, { params: cleanParams({ ...query }) });
  }
}
