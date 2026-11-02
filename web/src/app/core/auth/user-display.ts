import { Role } from './auth.models';

/** "Dana Driver" -> "DD", "madonna" -> "M": the letters shown in the avatar circle. */
export function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  const first = parts[0][0];
  const last = parts.length > 1 ? parts[parts.length - 1][0] : '';
  return (first + last).toUpperCase();
}

/** The role shown next to the name. A user with several roles is shown as the most powerful one. */
export function primaryRole(roles: readonly Role[]): Role {
  if (roles.includes('Admin')) return 'Admin';
  if (roles.includes('Attendant')) return 'Attendant';
  return 'Driver';
}
