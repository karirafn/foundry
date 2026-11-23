import { currentStep, toLifecycleStep } from './lifecycle.model';

describe('toLifecycleStep', () => {
  it('keeps a known actor, status and phase', () => {
    // Act
    const step = toLifecycleStep({
      step: 'publish',
      actor: 'code',
      status: 'current',
      phase: 'retrying',
    });

    // Assert
    expect(step).toEqual({ step: 'publish', actor: 'code', status: 'current', phase: 'retrying' });
  });

  it('reads an unknown actor as Foundry code', () => {
    // Act
    const step = toLifecycleStep({
      step: 'implement',
      actor: 'robot',
      status: 'done',
      phase: null,
    });

    // Assert
    expect(step.actor).toBe('code');
  });

  it('reads an unknown status as upcoming', () => {
    // Act
    const step = toLifecycleStep({
      step: 'implement',
      actor: 'claude',
      status: 'paused',
      phase: null,
    });

    // Assert
    expect(step.status).toBe('upcoming');
  });

  it.each([['sleeping'], [undefined], [null]])('reads the phase %j as none', (phase) => {
    // Act
    const step = toLifecycleStep({ step: 'implement', actor: 'claude', status: 'current', phase });

    // Assert
    expect(step.phase).toBeNull();
  });

  it('keeps a step token it has no copy for', () => {
    // Act
    const step = toLifecycleStep({
      step: 'mark_ready',
      actor: 'code',
      status: 'upcoming',
      phase: null,
    });

    // Assert
    expect(step.step).toBe('mark_ready');
  });
});

describe('currentStep', () => {
  it('finds the current step', () => {
    // Act
    const step = currentStep([
      { step: 'triage', actor: 'claude', status: 'done', phase: null },
      { step: 'implement', actor: 'claude', status: 'current', phase: 'running' },
    ]);

    // Assert
    expect(step?.step).toBe('implement');
  });

  it('is null without a lifecycle', () => {
    // Act / Assert
    expect(currentStep(undefined)).toBeNull();
  });
});
