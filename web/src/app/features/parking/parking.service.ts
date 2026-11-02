import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, map, of, switchMap } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api.config';
import { Lot, LotAvailability, LotBoard, LotOverview, PagedResult } from './parking.models';

@Injectable({ providedIn: 'root' })
export class ParkingService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  getLots(): Observable<Lot[]> {
    return this.http
      .get<PagedResult<Lot>>(`${this.base}/lots`, { params: { pageSize: 100 } })
      .pipe(map((page) => page.items));
  }

  getAvailability(lotId: string): Observable<LotAvailability> {
    return this.http.get<LotAvailability>(`${this.base}/lots/${lotId}/availability`);
  }

  getBoard(lotId: string): Observable<LotBoard> {
    return this.http.get<LotBoard>(`${this.base}/lots/${lotId}/board`);
  }

  /** Every lot with its live counts. One request for the list, then the availability calls run in parallel. */
  getOverview(): Observable<LotOverview[]> {
    return this.getLots().pipe(
      switchMap((lots) =>
        lots.length === 0
          ? of([])
          : forkJoin(
              lots.map((lot) =>
                this.getAvailability(lot.id).pipe(map((availability) => ({ lot, availability }))),
              ),
            ),
      ),
    );
  }
}
