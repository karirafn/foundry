import { getFailureCategoryDisplay, FAILURE_CATEGORY_DISPLAY } from './failure-category';

describe('failure-category', () => {
  // Cycle 1: known categories return display data
  it('should return display for non_zero_exit', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('non_zero_exit');

    // Assert
    expect(display).not.toBeNull();
    expect(display?.label).toBe('NON-ZERO EXIT');
  });

  it('should return display for timed_out', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('timed_out');

    // Assert
    expect(display).not.toBeNull();
    expect(display?.label).toBe('TIMED OUT');
  });

  it('should return display for container_error', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('container_error');

    // Assert
    expect(display).not.toBeNull();
    expect(display?.label).toBe('CONTAINER ERROR');
  });

  it('should return display for usage_limited', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('usage_limited');

    // Assert
    expect(display).not.toBeNull();
    expect(display?.label).toBe('USAGE LIMITED');
  });

  it('should return display for worker_bootstrap_failed', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('worker_bootstrap_failed');

    // Assert
    expect(display).not.toBeNull();
    expect(display?.label).toBe('BOOTSTRAP FAILED');
  });

  it('should return display for provider_error', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('provider_error');

    // Assert
    expect(display).not.toBeNull();
    expect(display?.label).toBe('PROVIDER ERROR');
  });

  it('should return badge--failure-provider-error cssClass for provider_error', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('provider_error');

    // Assert
    expect(display?.cssClass).toBe('badge--failure-provider-error');
  });

  // Cycle 2: unknown category returns null
  it('should return null for an unknown category token', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('unknown_future_category');

    // Assert
    expect(display).toBeNull();
  });

  // Cycle 3: all known categories have cssClass
  it('should include a cssClass for every known category', () => {
    // Arrange / Act
    const tokens = Object.keys(FAILURE_CATEGORY_DISPLAY);

    // Assert
    for (const token of tokens) {
      const display = getFailureCategoryDisplay(token);
      expect(display?.cssClass).toBeTruthy();
    }
  });
});
