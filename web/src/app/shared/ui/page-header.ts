import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** The same title block on every page: heading, one line of context, and room for actions on the right. */
@Component({
  selector: 'app-page-header',
  template: `
    <header class="page-header">
      <div class="titles">
        <h1>{{ title() }}</h1>
        @if (subtitle()) {
          <p>{{ subtitle() }}</p>
        }
      </div>
      <div class="actions"><ng-content /></div>
    </header>
  `,
  styles: `
    .page-header {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: 1rem;
      margin-bottom: 1.5rem;
    }
    h1 {
      margin: 0;
      font: var(--mat-sys-headline-medium);
      letter-spacing: -0.01em;
    }
    p {
      margin: 0.25rem 0 0;
      color: var(--mat-sys-on-surface-variant);
    }
    .actions {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input('');
}
