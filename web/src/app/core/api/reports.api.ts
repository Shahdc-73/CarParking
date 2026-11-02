import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { OccupancyReport, PerformanceReport } from '../../shared/models/report.models';
import { API_BASE_URL } from './api.config';
import { cleanParams } from './paging';

@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/reports`;

  occupancy(): Observable<OccupancyReport> {
    return this.http.get<OccupancyReport>(`${this.base}/occupancy`);
  }

  /** `from`/`to` are LOCAL calendar days (yyyy-mm-dd); the offset tells the server which day or hour a moment belongs to. */
  performance(from: string, to: string, lotId: string): Observable<PerformanceReport> {
    return this.http.get<PerformanceReport>(`${this.base}/performance`, {
      params: cleanParams({ from, to, lotId, utcOffsetMinutes: -new Date().getTimezoneOffset() }),
    });
  }
}
