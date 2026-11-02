import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { EmptyState } from '../../shared/ui/status';

@Component({
  selector: 'app-forbidden',
  imports: [EmptyState, MatButtonModule, RouterLink],
  template: `
    <app-empty-state
      icon="lock"
      title="You don't have access to this page"
      message="Your account's role doesn't include this area."
    />
    <p class="actions"><a mat-flat-button routerLink="/">Back to my home</a></p>
  `,
  styles: `
    .actions {
      text-align: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Forbidden {}
