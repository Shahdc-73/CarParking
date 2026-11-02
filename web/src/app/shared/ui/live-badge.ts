import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** Tells people whether the screen updates by itself right now, and how. Honest about the fallback. */
@Component({
  selector: 'app-live-badge',
  imports: [MatIconModule],
  template: `
    <span class="badge" [attr.data-mode]="mode()" role="status">
      <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
      {{ label() }}
    </span>
  `,
  styles: `
    .badge {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      padding: 0.125rem 0.625rem 0.125rem 0.375rem;
      border-radius: 999px;
      font: var(--mat-sys-label-medium);
      background: var(--mat-sys-surface-container-high);
      color: var(--mat-sys-on-surface-variant);
    }
    mat-icon {
      width: 1.125rem;
      height: 1.125rem;
      font-size: 1.125rem;
    }
    [data-mode='live'] {
      background: var(--pw-free-bg);
      color: var(--pw-free-fg);
    }
    [data-mode='reconnecting'] {
      background: var(--pw-reserved-bg);
      color: var(--pw-reserved-fg);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LiveBadge {
  readonly mode = input.required<'live' | 'reconnecting' | 'polling'>();

  protected icon(): string {
    const m = this.mode();
    return m === 'live' ? 'sensors' : m === 'reconnecting' ? 'sync_problem' : 'update';
  }

  protected label(): string {
    const m = this.mode();
    return m === 'live'
      ? 'Live'
      : m === 'reconnecting'
        ? 'Reconnecting…'
        : 'Refreshes every 30 seconds';
  }
}
