import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { ConfirmDialogService } from '../../../shared/components/confirm-dialog/confirm-dialog.service';

export interface UnsavedChanges {
  hasUnsavedChanges(): boolean;
}

const UNSAVED_CHANGES_MESSAGE = 'You have unsaved changes. Leave this page and discard them?';

export const unsavedChangesGuard: CanDeactivateFn<UnsavedChanges> = (component) => {
  if (!component.hasUnsavedChanges()) {
    return true;
  }
  return inject(ConfirmDialogService).confirm({
    title: 'Discard unsaved changes?',
    message: UNSAVED_CHANGES_MESSAGE,
    confirmLabel: 'Discard changes',
    cancelLabel: 'Stay on page',
    destructive: true,
  });
};
