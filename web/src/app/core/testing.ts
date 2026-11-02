import { AuthResponse, User } from './auth/auth.models';

export const driver: User = {
  id: 'u-1',
  email: 'dana@parkwise.local',
  fullName: 'Dana Driver',
  roles: ['Driver'],
  isActive: true,
};
export const attendant: User = {
  id: 'u-2',
  email: 'sam@parkwise.local',
  fullName: 'Sam Attendant',
  roles: ['Attendant'],
  isActive: true,
};
export const admin: User = {
  id: 'u-3',
  email: 'alex@parkwise.local',
  fullName: 'Alex Admin',
  roles: ['Admin'],
  isActive: true,
};

export function authResponse(token = 'token-1', user: User = driver): AuthResponse {
  return { accessToken: token, expiresAtUtc: '2030-01-01T00:00:00Z', user };
}
