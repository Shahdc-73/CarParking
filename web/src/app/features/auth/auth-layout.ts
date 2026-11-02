import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** Frame shared by sign-in and register: a brand panel on wide screens, a compact brand bar on phones. */
@Component({
  selector: 'app-auth-layout',
  imports: [MatIconModule],
  template: `
    <div class="page">
      <aside class="brand-panel">
        <div class="brand">
          <span class="logo" aria-hidden="true"><mat-icon>local_parking</mat-icon></span>
          <span>ParkWise</span>
        </div>

        <div class="pitch">
          <h2>Parking, without the guesswork.</h2>
          <p>Find a free spot, reserve it ahead, and pay without queueing.</p>
          <ul>
            <li>
              <mat-icon aria-hidden="true">bolt</mat-icon> See free spots live, by lot and type
            </li>
            <li>
              <mat-icon aria-hidden="true">event_available</mat-icon> Reserve a spot for when you
              arrive
            </li>
            <li>
              <mat-icon aria-hidden="true">receipt_long</mat-icon> Clear fees and a receipt for
              every visit
            </li>
          </ul>
        </div>

        <small>ParkWise demo project</small>
      </aside>

      <section class="form-panel">
        <div class="content">
          <div class="mobile-brand">
            <span class="logo" aria-hidden="true"><mat-icon>local_parking</mat-icon></span>
            <span>ParkWise</span>
          </div>
          <ng-content />
        </div>
      </section>
    </div>
  `,
  styleUrl: './auth-layout.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthLayout {}
