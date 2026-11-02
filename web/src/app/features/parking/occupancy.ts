import { SpotType } from './parking.models';

export type FillLevel = 'no-spots' | 'plenty' | 'busy' | 'almost-full' | 'full';

export interface FillStatus {
  level: FillLevel;
  label: string;
  icon: string;
}

/**
 * Turns two numbers into a status people understand at a glance. The label and icon carry the meaning, never colour alone
 * (about 1 in 12 men can't tell red from green).
 */
export function fillStatus(total: number, available: number): FillStatus {
  if (total === 0) return { level: 'no-spots', label: 'No spots yet', icon: 'inventory_2' };
  if (available <= 0) return { level: 'full', label: 'Full', icon: 'block' };

  const percentFull = ((total - available) / total) * 100;
  if (percentFull >= 85) return { level: 'almost-full', label: 'Almost full', icon: 'warning' };
  if (percentFull >= 60) return { level: 'busy', label: 'Filling up', icon: 'trending_up' };
  return { level: 'plenty', label: 'Plenty of space', icon: 'check_circle' };
}

export function percentFull(total: number, available: number): number {
  return total === 0 ? 0 : Math.round(((total - available) / total) * 100);
}

const TYPE_ICONS: Record<SpotType, string> = {
  Standard: 'directions_car',
  Compact: 'directions_car',
  EV: 'ev_station',
  Disabled: 'accessible',
  Motorcycle: 'two_wheeler',
};

export function spotTypeIcon(type: SpotType): string {
  return TYPE_ICONS[type];
}
