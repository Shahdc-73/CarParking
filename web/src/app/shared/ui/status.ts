import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';

/** Indeterminate loading indicator. `label` is what a screen reader announces. */
@Component({
  selector: 'app-loading',
  imports: [MatProgressBarModule],
  template: `<mat-progress-bar mode="indeterminate" [attr.aria-label]="label()" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Loading {
  readonly label = input('Loading');
}

/** An error the user can recover from. `role="alert"` makes screen readers announce it immediately. */
@Component({
  selector: 'app-error-banner',
  imports: [MatButtonModule, MatIconModule],
  template: `
    <div class="banner" role="alert">
      <mat-icon aria-hidden="true">error</mat-icon>
      <span class="message">{{ message() }}</span>
      @if (retryable()) {
        <button mat-button type="button" (click)="retry.emit()">Try again</button>
      }
    </div>
  `,
  styles: `
    .banner {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      padding: 0.75rem 1rem;
      border-radius: 0.75rem;
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
    .message {
      flex: 1 1 auto;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ErrorBanner {
  readonly message = input.required<string>();
  readonly retryable = input(true);
  readonly retry = output<void>();
}

@Component({
  selector: 'app-empty-state',
  imports: [MatIconModule],
  template: `
    <div class="empty">
      <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
      <h2>{{ title() }}</h2>
      @if (message()) {
        <p>{{ message() }}</p>
      }
    </div>
  `,
  styles: `
    .empty {
      display: grid;
      justify-items: center;
      gap: 0.25rem;
      padding: 3rem 1rem;
      text-align: center;
      color: var(--mat-sys-on-surface-variant);
    }
    mat-icon {
      font-size: 3rem;
      width: 3rem;
      height: 3rem;
    }
    h2 {
      margin: 0.5rem 0 0;
      font: var(--mat-sys-title-large);
      color: var(--mat-sys-on-surface);
    }
    p {
      margin: 0;
      max-width: 28rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyState {
  readonly icon = input('inbox');
  readonly title = input.required<string>();
  readonly message = input('');
}
