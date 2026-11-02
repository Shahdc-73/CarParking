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
        title: 'My parking · ParkWise',
        canActivate: [roleGuard('Driver')],
        loadComponent: () =>
          import('./features/reservations/driver-home').then((m) => m.DriverHome),
      },
      {
        path: 'admin',
        title: 'Administration · ParkWise',
        canActivate: [roleGuard('Admin')],
        loadComponent: () => import('./features/admin/admin-home').then((m) => m.AdminHome),
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
