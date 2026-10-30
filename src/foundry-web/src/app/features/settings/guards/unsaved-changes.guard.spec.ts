import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { unsavedChangesGuard, UnsavedChanges } from './unsaved-changes.guard';

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
  // Cycle 1: returns true when component has no unsaved changes (clean)
  it('should return true when the component has no unsaved changes', () => {
    // Arrange
    const component = makeComponent(false);

    // Act
    const result = runGuard(component);

    // Assert
    expect(result).toBe(true);
  });

  // Cycle 2: consults window.confirm when the component has unsaved changes and honours true
  it('should call window.confirm and return true when the user confirms leaving', () => {
    // Arrange
    const component = makeComponent(true);
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    // Act
    const result = runGuard(component);

    // Assert
    expect(window.confirm).toHaveBeenCalledWith(
      'You have unsaved changes. Leave this page and discard them?',
    );
    expect(result).toBe(true);
  });

  // Cycle 3: consults window.confirm and returns false when the user cancels
  it('should call window.confirm and return false when the user cancels', () => {
    // Arrange
    const component = makeComponent(true);
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    // Act
    const result = runGuard(component);

    // Assert
    expect(window.confirm).toHaveBeenCalledWith(
      'You have unsaved changes. Leave this page and discard them?',
    );
    expect(result).toBe(false);
  });
});
