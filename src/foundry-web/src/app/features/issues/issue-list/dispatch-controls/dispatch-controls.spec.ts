import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { DispatchControlsComponent } from './dispatch-controls';
import { DispatchService } from '../../../../core/services/dispatch.service';

interface DispatchServiceOverrides {
  isDispatchPaused?: boolean;
  usageLimitResetsAt?: string | null;
  pausing?: boolean;
  resuming?: boolean;
  pauseResumeError?: string | null;
}

function createMockDispatchService(overrides: DispatchServiceOverrides = {}) {
  const isPausedSignal = signal(overrides.isDispatchPaused ?? false);
  const usageLimitSignal = signal(overrides.usageLimitResetsAt ?? null);
  const pausingSignal = signal(overrides.pausing ?? false);
  const resumingSignal = signal(overrides.resuming ?? false);
  const pauseResumeErrorSignal = signal(overrides.pauseResumeError ?? null);
  const pauseDispatch = vi.fn();
  const resumeDispatch = vi.fn();

  return {
    isDispatchPaused: isPausedSignal.asReadonly(),
    usageLimitResetsAt: usageLimitSignal.asReadonly(),
    pausing: pausingSignal.asReadonly(),
    resuming: resumingSignal.asReadonly(),
    pauseResumeError: pauseResumeErrorSignal.asReadonly(),
    pauseDispatch,
    resumeDispatch,
    _isPausedSignal: isPausedSignal,
    _usageLimitSignal: usageLimitSignal,
    _pausingSignal: pausingSignal,
    _resumingSignal: resumingSignal,
    _pauseResumeErrorSignal: pauseResumeErrorSignal,
  };
}

function setup(overrides: DispatchServiceOverrides = {}) {
  const mockDispatch = createMockDispatchService(overrides);

  TestBed.configureTestingModule({
    imports: [DispatchControlsComponent],
    providers: [
      { provide: DispatchService, useValue: mockDispatch },
    ],
  });

  const fixture = TestBed.createComponent(DispatchControlsComponent);
  fixture.detectChanges();
  return { fixture, mockDispatch };
}

describe('DispatchControlsComponent', () => {
  // Cycle 1: "Pause All" button always visible
  it('should render the "Pause All" button', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.dispatch-controls__pause-btn');
    expect(btn).not.toBeNull();
    expect(btn?.textContent?.trim()).toBe('Pause All');
  });

  // Cycle 2: "Resume All" button hidden when not paused and no usage limit
  it('should not render the "Resume All" button when dispatch is not paused and there is no usage limit', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: false, usageLimitResetsAt: null });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.dispatch-controls__resume-btn');
    expect(btn).toBeNull();
  });

  // Cycle 3: "Resume All" button visible when isDispatchPaused is true
  it('should render the "Resume All" button when dispatch is paused', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: true });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.dispatch-controls__resume-btn');
    expect(btn).not.toBeNull();
    expect(btn?.textContent?.trim()).toBe('Resume All');
  });

  // Cycle 4: "Resume All" visible when usageLimitResetsAt is set
  it('should render the "Resume All" button when usageLimitResetsAt is set even if not paused', () => {
    // Arrange / Act
    const futureDate = new Date(Date.now() + 60_000).toISOString();
    const { fixture } = setup({ isDispatchPaused: false, usageLimitResetsAt: futureDate });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.dispatch-controls__resume-btn');
    expect(btn).not.toBeNull();
  });

  // Cycle 5: clicking "Pause All" calls pauseDispatch
  it('should call pauseDispatch when "Pause All" is clicked', () => {
    // Arrange
    const { fixture, mockDispatch } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Act
    const btn = el.querySelector('.dispatch-controls__pause-btn') as HTMLButtonElement;
    btn.click();

    // Assert
    expect(mockDispatch.pauseDispatch).toHaveBeenCalledTimes(1);
  });

  // Cycle 6: clicking "Resume All" calls resumeDispatch
  it('should call resumeDispatch when "Resume All" is clicked', () => {
    // Arrange
    const { fixture, mockDispatch } = setup({ isDispatchPaused: true });
    const el = fixture.nativeElement as HTMLElement;

    // Act
    const btn = el.querySelector('.dispatch-controls__resume-btn') as HTMLButtonElement;
    btn.click();

    // Assert
    expect(mockDispatch.resumeDispatch).toHaveBeenCalledTimes(1);
  });

  // Cycle 7: "Pause All" disabled while pausing
  it('should disable "Pause All" and show "Pausing..." while pausing', () => {
    // Arrange / Act
    const { fixture } = setup({ pausing: true });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.dispatch-controls__pause-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    expect(btn.textContent?.trim()).toBe('Pausing...');
  });

  // Cycle 7b: "Pause All" disabled when dispatch is already paused
  it('should disable "Pause All" when dispatch is already paused', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: true });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.dispatch-controls__pause-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 7c: "Pause All" disabled when usage limit is active
  it('should disable "Pause All" when usage limit is active', () => {
    // Arrange
    const futureDate = new Date(Date.now() + 60_000).toISOString();

    // Act
    const { fixture } = setup({ usageLimitResetsAt: futureDate });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.dispatch-controls__pause-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 8: "Resume All" disabled while resuming
  it('should disable "Resume All" and show "Resuming..." while resuming', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: true, resuming: true });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.dispatch-controls__resume-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    expect(btn.textContent?.trim()).toBe('Resuming...');
  });

  // Cycle 9: error message displayed when pauseResumeError is set
  it('should display the error message when pauseResumeError is set', () => {
    // Arrange / Act
    const { fixture } = setup({ pauseResumeError: 'Failed to pause dispatch' });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const errorSpan = el.querySelector('.dispatch-controls__error');
    expect(errorSpan).not.toBeNull();
    expect(errorSpan?.textContent?.trim()).toBe('Failed to pause dispatch');
  });

  it('should not display an error span when pauseResumeError is null', () => {
    // Arrange / Act
    const { fixture } = setup({ pauseResumeError: null });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const errorSpan = el.querySelector('.dispatch-controls__error');
    expect(errorSpan).toBeNull();
  });

  // Status span
  it('should show "Dispatch paused — usage limit" status when usage limit is active', () => {
    // Arrange
    const futureDate = new Date(Date.now() + 60_000).toISOString();

    // Act
    const { fixture } = setup({ usageLimitResetsAt: futureDate });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const status = el.querySelector('.dispatch-controls__status') as HTMLElement;
    expect(status?.textContent?.trim()).toBe('Dispatch paused — usage limit');
  });

  it('should show "Dispatch paused" status when manually paused without usage limit', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: true, usageLimitResetsAt: null });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const status = el.querySelector('.dispatch-controls__status') as HTMLElement;
    expect(status?.textContent?.trim()).toBe('Dispatch paused');
  });

  it('should show empty status text when dispatch is active', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: false, usageLimitResetsAt: null });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const status = el.querySelector('.dispatch-controls__status') as HTMLElement;
    expect(status?.textContent?.trim()).toBe('');
  });

  // Accessibility
  it('should have role="group" and aria-label on the wrapper', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('.dispatch-controls') as HTMLElement;
    expect(wrapper?.getAttribute('role')).toBe('group');
    expect(wrapper?.getAttribute('aria-label')).toBe('Dispatch controls');
  });

  it('should have type="button" on the "Pause All" button', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.dispatch-controls__pause-btn') as HTMLButtonElement;
    expect(btn?.type).toBe('button');
  });

  it('should have role="alert" on the error span', () => {
    // Arrange / Act
    const { fixture } = setup({ pauseResumeError: 'Some error' });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const errorSpan = el.querySelector('.dispatch-controls__error') as HTMLElement;
    expect(errorSpan?.getAttribute('role')).toBe('alert');
  });
});
