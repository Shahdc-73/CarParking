export interface LotOccupancy {
  lotId: string;
  lotName: string;
  isActive: boolean;
  total: number;
  available: number;
  occupied: number;
  reserved: number;
  outOfService: number;
  /** 0 to 1; out-of-service spots are not counted as usable. */
  occupancyRate: number;
}

export interface OccupancyReport {
  generatedAtUtc: string;
  overall: LotOccupancy;
  lots: LotOccupancy[];
}

export interface RevenueDay {
  date: string;
  amount: number;
  sessions: number;
}

export interface PeakHour {
  hour: number;
  checkIns: number;
}

export interface PerformanceReport {
  from: string;
  to: string;
  lotId: string | null;
  utcOffsetMinutes: number;
  totalRevenue: number;
  paidSessions: number;
  lostTickets: number;
  checkIns: number;
  averageStayMinutes: number | null;
  longestStayMinutes: number | null;
  revenueByDay: RevenueDay[];
  peakHours: PeakHour[];
}
