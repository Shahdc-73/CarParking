import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FeaturePlaceholder, PlannedFeature } from '../../shared/ui/feature-placeholder';

@Component({
  selector: 'app-driver-home',
  imports: [FeaturePlaceholder],
  template: `<app-feature-placeholder
    title="My parking"
    intro="Your vehicles, reservations and parking history."
    [items]="items"
  />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DriverHome {
  protected readonly items: PlannedFeature[] = [
    {
      icon: 'directions_car',
      title: 'My vehicles',
      description: 'Register the cars and motorcycles you park.',
    },
    {
      icon: 'event_available',
      title: 'Reservations',
      description: 'Reserve a spot for a time window and cancel if plans change.',
    },
    {
      icon: 'receipt_long',
      title: 'History and receipts',
      description: 'Past visits, what you paid, and a receipt for each.',
    },
  ];
}
