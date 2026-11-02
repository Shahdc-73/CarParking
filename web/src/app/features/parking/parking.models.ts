export type SpotType = 'Standard' | 'Compact' | 'EV' | 'Disabled' | 'Motorcycle';

export const SPOT_TYPES: readonly SpotType[] = [
  'Standard',
  'Compact',
  'EV',
  'Disabled',
  'Motorcycle',
];

export type { PagedResult } from '../../core/api/paging';

export interface Lot {
  id: string;
  name: string;
  address: string;
  isActive: boolean;
  allowMotorcycleOverflow: boolean;
  levelCount: number;
  spotCount: number;
}

export interface AvailabilityCounts {
  total: number;
  available: number;
  occupied: number;
  reserved: number;
  outOfService: number;
}

export interface TypeAvailability {
  type: SpotType;
  counts: AvailabilityCounts;
}

export interface LotAvailability {
  lotId: string;
  lotName: string;
  totals: AvailabilityCounts;
  byType: TypeAvailability[];
}

export interface LotOverview {
  lot: Lot;
  availability: LotAvailability;
}

export type SpotStatus = 'Available' | 'Occupied' | 'Reserved' | 'OutOfService';

/** Present only for occupied spots, and only when the server decided the caller may see it (staff, or the driver's own car). */
export interface SpotOccupancy {
  sessionId: string;
  checkInAtUtc: string;
  plateNumber: string;
  feeSoFar: number | null;
  freeMinutes: number | null;
  isMine: boolean;
}

export interface BoardSpot {
  id: string;
  code: string;
  type: SpotType;
  status: SpotStatus;
  occupancy: SpotOccupancy | null;
  holdExpiresAtUtc: string | null;
}

export interface BoardLevel {
  id: string;
  number: number;
  name: string;
  spots: BoardSpot[];
}

export interface LotBoard {
  lotId: string;
  lotName: string;
  isActive: boolean;
  /** The SERVER's clock when this was produced: used to correct for a wrong clock in the browser. */
  generatedAtUtc: string;
  totals: AvailabilityCounts;
  levels: BoardLevel[];
}
