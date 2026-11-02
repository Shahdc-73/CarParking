import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

/** Landing page for an area whose screens are built in the next phase: honest about what is coming, and proves the role routing works. */
@Component({
  selector: 'app-feature-placeholder',
  imports: [MatCardModule, MatIconModule],
  template: `
    <header>
      <h1>{{ title() }}</h1>
      <p class="intro">{{ intro() }}</p>
    </header>

    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>Coming next</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <ul>
          @for (item of items(); track item) {
            <li><mat-icon aria-hidden="true">check_circle_outline</mat-icon>{{ item }}</li>
          }
        </ul>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    h1 {
      font: var(--mat-sys-headline-medium);
      margin: 0 0 0.25rem;
    }
    .intro {
      margin: 0 0 1.5rem;
      color: var(--mat-sys-on-surface-variant);
    }
    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    li {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeaturePlaceholder {
  readonly title = input.required<string>();
  readonly intro = input.required<string>();
  readonly items = input<readonly string[]>([]);
}
