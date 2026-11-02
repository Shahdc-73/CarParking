import { ChipTone } from '../../shared/ui/chip';
import { SpotStatus } from './parking.models';

export interface SpotStatusView {
  label: string;
  icon: string;
  tone: ChipTone;
}

/** The words, icon and tone of each spot status: one definition for the live board and for admin screens. */
export const SPOT_STATUS: Record<SpotStatus, SpotStatusView> = {
  Available: { label: 'Free', icon: 'check_circle', tone: 'success' },
  Occupied: { label: 'Occupied', icon: 'directions_car', tone: 'danger' },
  Reserved: { label: 'Reserved', icon: 'schedule', tone: 'warning' },
  OutOfService: { label: 'Out of service', icon: 'block', tone: 'neutral' },
};
