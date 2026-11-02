import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { PageHeader } from '../../shared/ui/page-header';

/** The driver's area: three sections behind a tab bar. Each tab is a real route, so links, back button and reload all work. */
@Component({
  selector: 'app-driver-home',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatTabsModule, PageHeader],
  template: `
    <app-page-header
      title="My parking"
      subtitle="Your vehicles, reservations and parking history."
    />

    <nav mat-tab-nav-bar [tabPanel]="panel" aria-label="My parking sections">
      @for (tab of tabs; track tab.path) {
        <a
          mat-tab-link
          [routerLink]="tab.path"
          routerLinkActive
          #active="routerLinkActive"
          [active]="active.isActive"
          >{{ tab.label }}</a
        >
      }
    </nav>
    <mat-tab-nav-panel #panel class="panel">
      <router-outlet />
    </mat-tab-nav-panel>
  `,
  styles: `
    .panel {
      display: block;
      padding-top: 1.5rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DriverHome {
  protected readonly tabs = [
    { path: 'vehicles', label: 'My vehicles' },
    { path: 'reservations', label: 'Reservations' },
    { path: 'history', label: 'History' },
  ];
}
