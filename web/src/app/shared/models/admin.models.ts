import { SpotType } from '../../features/parking/parking.models';
import { Role } from '../../core/auth/auth.models';

export interface AdminUser {
  id: string;
  email: string;
  fullName: string;
  roles: Role[];
  isActive: boolean;
}

export interface CreateUserRequest {
  email: string;
  password: string;
  fullName: string;
  role: Role;
}

export interface AuditLog {
  id: string;
  actorUserId: string | null;
  action: string;
  entityType: string;
  entityId: string;
  beforeJson: string | null;
  afterJson: string | null;
  timestampUtc: string;
}

export interface SaveLotRequest {
  name: string;
  address: string;
  allowMotorcycleOverflow: boolean;
}

export interface LotLevel {
  id: string;
  lotId: string;
  number: number;
  name: string;
  spotCount: number;
}

export interface LotDetail {
  id: string;
  name: string;
  address: string;
  isActive: boolean;
  allowMotorcycleOverflow: boolean;
  levels: LotLevel[];
}

export interface SaveSpotRequest {
  code: string;
  type: SpotType;
}
