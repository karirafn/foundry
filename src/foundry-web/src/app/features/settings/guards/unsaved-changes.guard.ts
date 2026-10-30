import { CanDeactivateFn } from '@angular/router';

export interface UnsavedChanges {
  hasUnsavedChanges(): boolean;
}

const UNSAVED_CHANGES_MESSAGE = 'You have unsaved changes. Leave this page and discard them?';

export const unsavedChangesGuard: CanDeactivateFn<UnsavedChanges> = (component) => {
  if (!component.hasUnsavedChanges()) {
    return true;
  }
  return window.confirm(UNSAVED_CHANGES_MESSAGE);
};
