import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FeaturePlaceholder, PlannedFeature } from '../../shared/ui/feature-placeholder';

@Component({
  selector: 'app-gate-home',
  imports: [FeaturePlaceholder],
  template: `<app-feature-placeholder
    title="Gate"
    intro="Check vehicles in and out, and handle lost tickets."
    [items]="items"
  />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GateHome {
  protected readonly items: PlannedFeature[] = [
    {
      icon: 'login',
      title: 'Check in',
      description: 'Enter a plate number and a spot is assigned automatically.',
    },
    {
      icon: 'logout',
      title: 'Check out',
      description: 'See the fee, take payment and print the receipt.',
    },
    {
      icon: 'confirmation_number',
      title: 'Lost ticket',
      description: 'Close a session with the flat lost-ticket fee.',
    },
  ];
}
