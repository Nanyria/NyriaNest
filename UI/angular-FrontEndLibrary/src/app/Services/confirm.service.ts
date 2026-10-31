import { Injectable, signal } from '@angular/core';

export interface ConfirmOptions {
  title: string;
  message?: string;
  confirmLabel: string; // say what happens, e.g. "Delete", not "OK"
  cancelLabel?: string; // defaults to "Cancel"
  danger?: boolean; // red confirm button, for things that can't be undone
  icon?: string; // Bootstrap icon name without "bi-"; match the button that was clicked
}

interface OpenConfirm extends ConfirmOptions {
  resolve: (confirmed: boolean) => void;
}

// Styled replacement for window.confirm(), shown by the ConfirmDialogComponent
// in the app shell. Resolves true for the confirm button, false for Cancel,
// Esc or a click outside.
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  readonly current = signal<OpenConfirm | null>(null);

  confirm(options: ConfirmOptions): Promise<boolean> {
    this.current()?.resolve(false); // a new question replaces one still open
    return new Promise((resolve) => this.current.set({ ...options, resolve }));
  }

  close(confirmed: boolean): void {
    const open = this.current();
    if (!open) return;
    this.current.set(null);
    open.resolve(confirmed);
  }
}
