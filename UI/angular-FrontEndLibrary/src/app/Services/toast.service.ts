import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
  icon: string; // Bootstrap icon name without "bi-", e.g. 'trash3'
}

const TOAST_DURATION_MS = 5000;

// Short messages ("Book saved", "Couldn't borrow the book") shown by the
// ToastComponent in the app shell, so they survive navigating from one page to another
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 0;
  readonly toasts = signal<Toast[]>([]);

  // The icon tells which action it was, and should match the icon on the
  // button the user clicked
  success(message: string, icon = 'check-lg'): void {
    this.show('success', message, icon);
  }

  // Something the user tried didn't work; red seal instead of green
  error(message: string, icon = 'exclamation-lg'): void {
    this.show('error', message, icon);
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }

  private show(kind: ToastKind, message: string, icon: string): void {
    const id = ++this.nextId;
    this.toasts.update((list) => [...list, { id, kind, message, icon }]);
    setTimeout(() => this.dismiss(id), TOAST_DURATION_MS);
  }
}
