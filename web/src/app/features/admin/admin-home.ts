import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FeaturePlaceholder } from '../../shared/ui/feature-placeholder';

@Component({
  selector: 'app-admin-home',
  imports: [FeaturePlaceholder],
  template: `<app-feature-placeholder
    title="Administration"
    intro="Manage lots, pricing and users, and see reports."
    [items]="[
      'Lots, levels and spots',
      'Pricing rules',
      'Users and roles',
      'Occupancy, revenue and peak-hour reports, plus the audit log',
    ]"
  />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminHome {}
