import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { PageHeader } from '../../shared/ui/page-header';

/** The admin area: one tab per concern. Each tab is a real route (so links and reload work). */
@Component({
  selector: 'app-admin-home',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatTabsModule, PageHeader],
  template: `
    <app-page-header
      title="Administration"
      subtitle="See how the lots perform, manage lots, pricing and users, and review changes."
    />

    <nav mat-tab-nav-bar [tabPanel]="panel" aria-label="Administration sections">
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
export class AdminHome {
  protected readonly tabs = [
    { path: 'dashboard', label: 'Dashboard' },
    { path: 'lots', label: 'Lots and spots' },
    { path: 'pricing', label: 'Pricing' },
    { path: 'users', label: 'Users' },
    { path: 'audit', label: 'Audit log' },
  ];
}
