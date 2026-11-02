import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

/** One place for "it worked" / "it failed" toasts, so they all look and behave the same (and are announced to screen readers). */
@Injectable({ providedIn: 'root' })
export class Notify {
  private readonly bar = inject(MatSnackBar);

  success(message: string): void {
    this.bar.open(message, 'Dismiss', { duration: 4000, politeness: 'polite' });
  }

  /** Errors stay a little longer, and are announced assertively. */
  error(message: string): void {
    this.bar.open(message, 'Dismiss', { duration: 8000, politeness: 'assertive' });
  }
}
