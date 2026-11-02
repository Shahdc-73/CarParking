import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FeaturePlaceholder } from '../../shared/ui/feature-placeholder';

@Component({
  selector: 'app-driver-home',
  imports: [FeaturePlaceholder],
  template: `<app-feature-placeholder
    title="My parking"
    intro="Your vehicles, reservations and parking history."
    [items]="[
      'Register and manage your vehicles',
      'Reserve a spot for a time window',
      'Parking history and receipts',
    ]"
  />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DriverHome {}
