import { Routes } from '@angular/router';
import { authGuard, guestGuard, homeFor, roleGuard } from './core/auth/auth.guards';
import { AuthService } from './core/auth/auth.service';
import { inject } from '@angular/core';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · ParkWise',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login').then((m) => m.Login),
  },
  {
    path: 'register',
    title: 'Create account · ParkWise',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/register').then((m) => m.Register),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      // "/" sends each role to its own home.
      {
        path: '',
        pathMatch: 'full',
        redirectTo: () => homeFor(inject(AuthService).user()?.roles ?? []),
      },
      {
        path: 'lots',
        title: 'Parking lots · ParkWise',
        loadComponent: () => import('./features/parking/lots-overview').then((m) => m.LotsOverview),
      },
      {
        path: 'lots/:id',
        title: 'Parking spots · ParkWise',
        loadComponent: () => import('./features/parking/lot-board').then((m) => m.LotBoardPage),
      },
      {
        path: 'gate',
        title: 'Gate · ParkWise',
        canActivate: [roleGuard('Admin', 'Attendant')],
        loadComponent: () => import('./features/gate/gate-home').then((m) => m.GateHome),
      },
      {
        path: 'my',
        canActivate: [roleGuard('Driver')],
        loadComponent: () =>
          import('./features/reservations/driver-home').then((m) => m.DriverHome),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'vehicles' },
          {
            path: 'vehicles',
            title: 'My vehicles · ParkWise',
            loadComponent: () =>
              import('./features/reservations/my-vehicles').then((m) => m.MyVehicles),
          },
          {
            path: 'reservations',
            title: 'My reservations · ParkWise',
            loadComponent: () =>
              import('./features/reservations/my-reservations').then((m) => m.MyReservations),
          },
          {
            path: 'history',
            title: 'My parking history · ParkWise',
            loadComponent: () => import('./features/history/my-history').then((m) => m.MyHistory),
          },
        ],
      },
      {
        path: 'admin',
        canActivate: [roleGuard('Admin')],
        loadComponent: () => import('./features/admin/admin-home').then((m) => m.AdminHome),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'lots' },
          {
            path: 'lots',
            title: 'Lots · ParkWise',
            loadComponent: () => import('./features/admin/admin-lots').then((m) => m.AdminLots),
          },
          {
            path: 'lots/:id',
            title: 'Manage lot · ParkWise',
            loadComponent: () =>
              import('./features/admin/admin-lot-detail').then((m) => m.AdminLotDetail),
          },
          {
            path: 'pricing',
            title: 'Pricing · ParkWise',
            loadComponent: () =>
              import('./features/admin/admin-pricing').then((m) => m.AdminPricing),
          },
          {
            path: 'users',
            title: 'Users · ParkWise',
            loadComponent: () => import('./features/admin/admin-users').then((m) => m.AdminUsers),
          },
          {
            path: 'audit',
            title: 'Audit log · ParkWise',
            loadComponent: () => import('./features/admin/admin-audit').then((m) => m.AdminAudit),
          },
        ],
      },
      {
        path: 'forbidden',
        title: 'No access · ParkWise',
        loadComponent: () => import('./features/errors/forbidden').then((m) => m.Forbidden),
      },
    ],
  },
  {
    path: '**',
    title: 'Not found · ParkWise',
    loadComponent: () => import('./features/errors/not-found').then((m) => m.NotFound),
  },
];
