import { SpotType } from '../../features/parking/parking.models';

export type ReservationStatus = 'Pending' | 'Fulfilled' | 'Expired' | 'Cancelled';

export interface Reservation {
  id: string;
  vehicleId: string;
  plateNumber: string;
  lotId: string;
  lotName: string;
  levelNumber: number;
  spotId: string;
  spotCode: string;
  spotType: SpotType;
  startUtc: string;
  endUtc: string;
  holdExpiresAtUtc: string;
  status: ReservationStatus;
  bookedByUserId: string;
}

export interface CreateReservationRequest {
  lotId: string;
  vehicleId: string;
  spotType: SpotType;
  startUtc: string;
  endUtc: string;
}

export interface ReservationAvailability {
  lotId: string;
  spotType: SpotType;
  startUtc: string;
  endUtc: string;
  availableSpots: number;
}

/** Rules mirrored from the API purely so the form can guide people; the server still enforces them. */
export const RESERVATION_DURATIONS: readonly { minutes: number; label: string }[] = [
  { minutes: 30, label: '30 minutes' },
  { minutes: 60, label: '1 hour' },
  { minutes: 120, label: '2 hours' },
  { minutes: 240, label: '4 hours' },
  { minutes: 480, label: '8 hours' },
  { minutes: 1440, label: '24 hours' },
];
