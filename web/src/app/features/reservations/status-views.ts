import { ChipTone } from '../../shared/ui/chip';
import { ReservationStatus } from '../../shared/models/reservation.models';
import { SessionStatus } from '../../shared/models/session.models';

export interface StatusView {
  label: string;
  tone: ChipTone;
  icon: string;
}

export const RESERVATION_STATUS: Record<ReservationStatus, StatusView> = {
  Pending: { label: 'Upcoming', tone: 'info', icon: 'schedule' },
  Fulfilled: { label: 'Used', tone: 'success', icon: 'check_circle' },
  Expired: { label: 'Missed', tone: 'warning', icon: 'timer_off' },
  Cancelled: { label: 'Cancelled', tone: 'neutral', icon: 'cancel' },
};

export const SESSION_STATUS: Record<SessionStatus, StatusView> = {
  Active: { label: 'Parked now', tone: 'info', icon: 'local_parking' },
  Completed: { label: 'Completed', tone: 'success', icon: 'check_circle' },
  LostTicket: { label: 'Lost ticket', tone: 'warning', icon: 'confirmation_number' },
};
