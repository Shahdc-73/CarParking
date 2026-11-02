import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { EmptyState } from '../../shared/ui/status';

@Component({
  selector: 'app-not-found',
  imports: [EmptyState, MatButtonModule, RouterLink],
  template: `
    <main class="page">
      <app-empty-state
        icon="search_off"
        title="Page not found"
        message="The page you're looking for doesn't exist or was moved."
      />
      <p class="actions"><a mat-flat-button routerLink="/">Go to the start page</a></p>
    </main>
  `,
  styles: `
    .page {
      max-width: 36rem;
      margin: 4rem auto;
      padding: 0 1rem;
    }
    .actions {
      text-align: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFound {}
