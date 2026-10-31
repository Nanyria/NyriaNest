import { CanDeactivateFn } from '@angular/router';

// Pages with a form implement this so the guard can ask before leaving
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

// Covers Cancel, links and the Back button. Closing or reloading the tab is
// handled by the page itself (beforeunload), since the router never sees it.
// TODO: swap confirm() for a styled dialog (guide step 5.2)
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) =>
  !component.hasUnsavedChanges() || confirm("Discard changes? What you've entered will be lost.");
