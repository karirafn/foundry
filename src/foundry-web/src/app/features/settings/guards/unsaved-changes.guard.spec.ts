import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { unsavedChangesGuard, UnsavedChanges } from './unsaved-changes.guard';
import { ConfirmDialogService } from '../../../shared/components/confirm-dialog/confirm-dialog.service';

function makeComponent(dirty: boolean): UnsavedChanges {
  return { hasUnsavedChanges: () => dirty };
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function runGuard(component: UnsavedChanges): any {
  return TestBed.runInInjectionContext(() =>
    unsavedChangesGuard(component, {} as never, {} as never, {} as never),
  );
}

describe('unsavedChangesGuard', () => {
  afterEach(() => vi.restoreAllMocks());

  it('should return true when the component has no unsaved changes', () => {
    // Arrange
    const component = makeComponent(false);
    const confirmSpy = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm');

    // Act
    const result = runGuard(component);

    // Assert
    expect(result).toBe(true);
    expect(confirmSpy).not.toHaveBeenCalled();
  });

  it('should confirm through the shared confirm dialog and allow leaving when confirmed', async () => {
    // Arrange
    const component = makeComponent(true);
    const confirmSpy = vi
      .spyOn(TestBed.inject(ConfirmDialogService), 'confirm')
      .mockResolvedValue(true);

    // Act
    const result = await runGuard(component);

    // Assert
    expect(confirmSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        message: 'You have unsaved changes. Leave this page and discard them?',
        destructive: true,
      }),
    );
    expect(result).toBe(true);
  });

  it('should block leaving when the confirm dialog is dismissed', async () => {
    // Arrange
    const component = makeComponent(true);
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);

    // Act
    const result = await runGuard(component);

    // Assert
    expect(result).toBe(false);
  });

  it('should never fall back to the native confirm prompt', async () => {
    // Arrange
    const component = makeComponent(true);
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    const nativeConfirm = vi.fn();
    vi.stubGlobal('confirm', nativeConfirm);

    // Act
    await runGuard(component);

    // Assert
    expect(nativeConfirm).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
});
