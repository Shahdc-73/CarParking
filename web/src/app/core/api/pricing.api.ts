import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SpotType } from '../../features/parking/parking.models';
import {
  LostTicketFee,
  PricingOverview,
  PricingRule,
  SavePricingRuleRequest,
} from '../../shared/models/pricing.models';
import { API_BASE_URL } from './api.config';

@Injectable({ providedIn: 'root' })
export class PricingApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/pricing`;

  current(): Observable<PricingOverview> {
    return this.http.get<PricingOverview>(this.base);
  }

  /** Adds a NEW price version; existing versions and old receipts are never changed. */
  setRule(spotType: SpotType, request: SavePricingRuleRequest): Observable<PricingRule> {
    return this.http.put<PricingRule>(`${this.base}/${spotType}`, request);
  }

  setLostTicketFee(amount: number, effectiveFromUtc?: string | null): Observable<LostTicketFee> {
    return this.http.put<LostTicketFee>(`${this.base}/lost-ticket-fee`, {
      amount,
      effectiveFromUtc,
    });
  }
}
