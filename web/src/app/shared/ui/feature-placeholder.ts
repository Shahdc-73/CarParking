import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { PageHeader } from './page-header';

export interface PlannedFeature {
  icon: string;
  title: string;
  description: string;
}

/** An area whose screens arrive in the next phase: shows exactly what will live here, as tiles, instead of an empty page. */
@Component({
  selector: 'app-feature-placeholder',
  imports: [MatIconModule, PageHeader],
  template: `
    <app-page-header [title]="title()" [subtitle]="intro()" />

    <ul class="tiles">
      @for (item of items(); track item.title) {
        <li class="tile">
          <span class="icon" aria-hidden="true"
            ><mat-icon>{{ item.icon }}</mat-icon></span
          >
          <div>
            <h2>{{ item.title }}</h2>
            <p>{{ item.description }}</p>
          </div>
          <span class="badge">Coming soon</span>
        </li>
      }
    </ul>
  `,
  styles: `
    .tiles {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 19rem), 1fr));
      gap: 1rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .tile {
      position: relative;
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 1rem;
      align-items: start;
      padding: 1.25rem;
      border: 1px dashed var(--mat-sys-outline-variant);
      border-radius: 1rem;
      background: var(--mat-sys-surface-container-lowest);
    }
    .icon {
      display: grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border-radius: 0.75rem;
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }
    h2 {
      margin: 0 0 0.25rem;
      font: var(--mat-sys-title-medium);
    }
    p {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-medium);
    }
    .badge {
      grid-column: 1 / -1;
      justify-self: start;
      padding: 0.125rem 0.625rem;
      border-radius: 999px;
      background: var(--mat-sys-surface-container-high);
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-label-small);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeaturePlaceholder {
  readonly title = input.required<string>();
  readonly intro = input.required<string>();
  readonly items = input<readonly PlannedFeature[]>([]);
}
