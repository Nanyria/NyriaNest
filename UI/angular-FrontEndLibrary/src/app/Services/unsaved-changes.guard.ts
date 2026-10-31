import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { ConfirmService } from './confirm.service';

// Pages with a form implement this so the guard can ask before leaving
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

// Covers Cancel, links and the Back button. Closing or reloading the tab is
// handled by the page itself (beforeunload), since the router never sees it.
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) =>
  !component.hasUnsavedChanges() ||
  inject(ConfirmService).confirm({
    title: 'Discard changes?',
    message: "What you've entered will be lost.",
    confirmLabel: 'Discard',
    cancelLabel: 'Keep editing',
    danger: true,
  });
