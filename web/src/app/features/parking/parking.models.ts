export type SpotType = 'Standard' | 'Compact' | 'EV' | 'Disabled' | 'Motorcycle';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

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
