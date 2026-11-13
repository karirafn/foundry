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

  it('should return display for model_unavailable', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('model_unavailable');

    // Assert
    expect(display?.label).toBe('MODEL UNAVAILABLE');
    expect(display?.cssClass).toBe('badge--failure-model-unavailable');
  });

  it('should return display for turn_limit', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('turn_limit');

    // Assert
    expect(display?.label).toBe('TURN LIMIT');
    expect(display?.cssClass).toBe('badge--failure-turn-limit');
  });

  it('should return display for auth_invalid', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('auth_invalid');

    // Assert
    expect(display?.label).toBe('AUTH FAILED');
    expect(display?.cssClass).toBe('badge--failure-auth-invalid');
  });

  it('should return display for transient_api_error', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('transient_api_error');

    // Assert
    expect(display?.label).toBe('CLAUDE API ERROR');
    expect(display?.cssClass).toBe('badge--failure-transient-api-error');
  });

  it('should return display for pr_closed', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('pr_closed');

    // Assert
    expect(display?.label).toBe('PR CLOSED');
    expect(display?.cssClass).toBe('badge--failure-pr-closed');
  });

  it('should return display for cancelled', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('cancelled');

    // Assert
    expect(display?.label).toBe('CANCELLED');
    expect(display?.cssClass).toBe('badge--failure-cancelled');
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

  it('should map provider_unreachable to "PROVIDER UNREACHABLE"', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('provider_unreachable');

    // Assert
    expect(display?.label).toBe('PROVIDER UNREACHABLE');
  });

  it('should return badge--failure-provider-unreachable cssClass for provider_unreachable', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('provider_unreachable');

    // Assert
    expect(display?.cssClass).toBe('badge--failure-provider-unreachable');
  });

  it('should return display for credits_exhausted', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('credits_exhausted');

    // Assert
    expect(display).not.toBeNull();
    expect(display?.label).toBe('CREDITS EXHAUSTED');
  });

  it('should return badge--failure-credits-exhausted cssClass for credits_exhausted', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('credits_exhausted');

    // Assert
    expect(display?.cssClass).toBe('badge--failure-credits-exhausted');
  });

  // Cycle 2: unknown category returns null
  it('should return display for verdict_invalid', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('verdict_invalid');

    // Assert
    expect(display?.label).toBe('INVALID VERDICT');
    expect(display?.cssClass).toBe('badge--failure-verdict-invalid');
  });

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

describe('publish failure categories (#787)', () => {
  it('should map publish_expired to "PUBLISH EXPIRED"', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('publish_expired');

    // Assert
    expect(display?.label).toBe('PUBLISH EXPIRED');
    expect(display?.cssClass).toBe('badge--failure-publish-expired');
  });

  it('should map publish_failed to "PUBLISH FAILED"', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('publish_failed');

    // Assert
    expect(display?.label).toBe('PUBLISH FAILED');
    expect(display?.cssClass).toBe('badge--failure-publish-failed');
  });
});

describe('discard failure category (#788)', () => {
  it('should map run_discarded to "RUN DISCARDED"', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('run_discarded');

    // Assert
    expect(display?.label).toBe('RUN DISCARDED');
    expect(display?.cssClass).toBe('badge--failure-cancelled');
  });
});

describe('issue-closed failure category (#909)', () => {
  it('should map issue_closed to "ISSUE CLOSED" with the provider-closed badge', () => {
    // Arrange / Act
    const display = getFailureCategoryDisplay('issue_closed');

    // Assert
    expect(display?.label).toBe('ISSUE CLOSED');
    expect(display?.cssClass).toBe('badge--failure-pr-closed');
  });
});
