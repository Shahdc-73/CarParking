import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  CheckInRequest,
  FeePreview,
  Receipt,
  Session,
  SessionStatus,
  SettlementResult,
} from '../../shared/models/session.models';
import { API_BASE_URL } from './api.config';
import { PagedResult, cleanParams } from './paging';

export interface SessionQuery {
  status?: SessionStatus | '';
  plate?: string;
  ticketCode?: string;
  lotId?: string;
  sort?: string;
  page?: number;
  pageSize?: number;
}

/** Everything about parking sessions: gate operations, history and receipts. */
@Injectable({ providedIn: 'root' })
export class SessionsApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  list(query: SessionQuery = {}): Observable<PagedResult<Session>> {
    return this.http.get<PagedResult<Session>>(`${this.base}/sessions`, {
      params: cleanParams({ ...query }),
    });
  }

  checkIn(request: CheckInRequest): Observable<Session> {
    return this.http.post<Session>(`${this.base}/sessions/check-in`, request);
  }

  feePreview(sessionId: string): Observable<FeePreview> {
    return this.http.get<FeePreview>(`${this.base}/sessions/${sessionId}/fee-preview`);
  }

  /** `idempotencyKey` makes a retry of THIS attempt safe: the server returns the same receipt instead of charging again. */
  checkOut(sessionId: string, idempotencyKey: string): Observable<SettlementResult> {
    return this.http
      .post<Receipt>(
        `${this.base}/sessions/${sessionId}/check-out`,
        {},
        { headers: { 'Idempotency-Key': idempotencyKey }, observe: 'response' },
      )
      .pipe(
        map((r) => ({
          receipt: r.body!,
          replayed: r.headers.get('Idempotent-Replayed') === 'true',
        })),
      );
  }

  lostTicket(plateNumber: string): Observable<SettlementResult> {
    return this.http
      .post<Receipt>(`${this.base}/sessions/lost-ticket`, { plateNumber }, { observe: 'response' })
      .pipe(
        map((r) => ({
          receipt: r.body!,
          replayed: r.headers.get('Idempotent-Replayed') === 'true',
        })),
      );
  }

  receipt(receiptId: string): Observable<Receipt> {
    return this.http.get<Receipt>(`${this.base}/receipts/${receiptId}`);
  }
}
