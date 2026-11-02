import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FeaturePlaceholder } from '../../shared/ui/feature-placeholder';

@Component({
  selector: 'app-gate-home',
  imports: [FeaturePlaceholder],
  template: `<app-feature-placeholder
    title="Gate"
    intro="Check vehicles in and out, and handle lost tickets."
    [items]="[
      'Check in by plate number with automatic spot assignment',
      'Check out with fee preview, payment and receipt',
      'Lost-ticket handling',
    ]"
  />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GateHome {}
