import { SpotType } from '../../features/parking/parking.models';

export interface PricingRule {
  spotType: SpotType;
  hourlyRate: number;
  dailyCap: number;
  freeMinutes: number;
  effectiveFromUtc: string;
}

export interface LostTicketFee {
  amount: number;
  effectiveFromUtc: string;
}

export interface PricingOverview {
  rules: PricingRule[];
  lostTicketFee: LostTicketFee | null;
}

export interface SavePricingRuleRequest {
  hourlyRate: number;
  dailyCap: number;
  freeMinutes: number;
  /** ISO UTC time the new price starts; omit for "now". */
  effectiveFromUtc?: string | null;
}
