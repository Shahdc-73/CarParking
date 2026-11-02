import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FeaturePlaceholder, PlannedFeature } from '../../shared/ui/feature-placeholder';

@Component({
  selector: 'app-admin-home',
  imports: [FeaturePlaceholder],
  template: `<app-feature-placeholder
    title="Administration"
    intro="Manage lots, pricing and users, and see reports."
    [items]="items"
  />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminHome {
  protected readonly items: PlannedFeature[] = [
    {
      icon: 'warehouse',
      title: 'Lots and spots',
      description: 'Add lots, levels and spots, and take spots out of service.',
    },
    {
      icon: 'payments',
      title: 'Pricing',
      description: 'Hourly rates, daily caps, free minutes and the lost-ticket fee.',
    },
    {
      icon: 'group',
      title: 'Users',
      description: 'Create attendants and admins, and deactivate accounts.',
    },
    {
      icon: 'monitoring',
      title: 'Reports and audit log',
      description: 'Occupancy, revenue, peak hours, and who changed what.',
    },
  ];
}
