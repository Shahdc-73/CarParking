import { SpotType } from '../../features/parking/parking.models';
import { VehicleType } from './vehicle.models';

export type SessionStatus = 'Active' | 'Completed' | 'LostTicket';

export interface Session {
  id: string;
  ticketCode: string;
  vehicleId: string;
  plateNumber: string;
  vehicleType: VehicleType;
  lotId: string;
  lotName: string;
  levelNumber: number;
  spotId: string;
  spotCode: string;
  spotType: SpotType;
  checkInAtUtc: string;
  checkOutAtUtc: string | null;
  status: SessionStatus;
  receiptId: string | null;
  receiptNumber: string | null;
  amountPaid: number | null;
  reservationId: string | null;
}

export interface CheckInRequest {
  lotId: string;
  plateNumber: string;
  vehicleType?: VehicleType | null;
}

/** How a fee was reached. `duration` is a .NET TimeSpan string ("01:45:00", "1.03:00:11"): see parseTimeSpan. */
export interface FeeBreakdown {
  total: number;
  duration: string;
  freeMinutes: number;
  hourlyRate: number;
  dailyCap: number;
  billedHours: number;
  daysStarted: number;
  daysCapped: number;
  isLostTicket: boolean;
}

export interface FeePreview {
  sessionId: string;
  atUtc: string;
  amount: number;
  breakdown: FeeBreakdown;
}

export interface Receipt {
  id: string;
  number: string;
  sessionId: string;
  ticketCode: string;
  plateNumber: string;
  lotName: string;
  levelNumber: number;
  spotCode: string;
  checkInAtUtc: string;
  checkOutAtUtc: string;
  amount: number;
  paymentMethod: string;
  issuedAtUtc: string;
  breakdown: FeeBreakdown;
}

export interface SettlementResult {
  receipt: Receipt;
  /** True when this check-out had already happened and the existing receipt was returned (nothing was charged again). */
  replayed: boolean;
}
