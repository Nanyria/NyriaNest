import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: number;
  message: string;
  icon: string; // Bootstrap icon name without "bi-", e.g. 'trash3'
}

const TOAST_DURATION_MS = 5000;

// Short success messages ("Book saved") shown by the ToastComponent in the app
// shell, so they survive navigating from one page to another
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 0;
  readonly toasts = signal<Toast[]>([]);

  // Always the success style; the icon tells which action it was, and should
  // match the icon on the button the user clicked
  success(message: string, icon = 'check-lg'): void {
    const id = ++this.nextId;
    this.toasts.update((list) => [...list, { id, message, icon }]);
    setTimeout(() => this.dismiss(id), TOAST_DURATION_MS);
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
