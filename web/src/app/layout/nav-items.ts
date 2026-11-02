import { Role } from '../core/auth/auth.models';

export interface NavItem {
  label: string;
  path: string;
  icon: string;
  /** Roles that can see this link. Empty means everyone who is signed in. */
  roles: readonly Role[];
}

export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Gate', path: '/gate', icon: 'garage_door', roles: ['Admin', 'Attendant'] },
  { label: 'My parking', path: '/my', icon: 'directions_car', roles: ['Driver'] },
  { label: 'Administration', path: '/admin', icon: 'admin_panel_settings', roles: ['Admin'] },
  { label: 'Parking lots', path: '/lots', icon: 'local_parking', roles: [] },
];

export function navItemsFor(roles: readonly Role[]): NavItem[] {
  return NAV_ITEMS.filter(
    (item) => item.roles.length === 0 || item.roles.some((r) => roles.includes(r)),
  );
}
