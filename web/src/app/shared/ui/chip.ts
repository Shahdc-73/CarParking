import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

export type ChipTone = 'success' | 'danger' | 'warning' | 'info' | 'neutral';

/** A small status label. Colour is only a hint: the icon and the text carry the meaning. */
@Component({
  selector: 'app-chip',
  imports: [MatIconModule],
  template: `<span class="chip" [attr.data-tone]="tone()"
    ><mat-icon aria-hidden="true">{{ icon() }}</mat-icon
    >{{ label() }}</span
  >`,
  styles: `
    .chip {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      padding: 0.125rem 0.625rem 0.125rem 0.375rem;
      border-radius: 999px;
      font: var(--mat-sys-label-medium);
      white-space: nowrap;
      background: var(--mat-sys-surface-container-high);
      color: var(--mat-sys-on-surface-variant);
    }
    mat-icon {
      width: 1.125rem;
      height: 1.125rem;
      font-size: 1.125rem;
    }
    [data-tone='success'] {
      background: var(--pw-free-bg);
      color: var(--pw-free-fg);
    }
    [data-tone='danger'] {
      background: var(--pw-occupied-bg);
      color: var(--pw-occupied-fg);
    }
    [data-tone='warning'] {
      background: var(--pw-reserved-bg);
      color: var(--pw-reserved-fg);
    }
    [data-tone='info'] {
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Chip {
  readonly label = input.required<string>();
  readonly tone = input<ChipTone>('neutral');
  readonly icon = input('circle');
}
