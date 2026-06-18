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

function getToggleBtn(fixture: { nativeElement: HTMLElement }): HTMLButtonElement {
  return fixture.nativeElement.querySelector('.dispatch-controls__toggle-btn') as HTMLButtonElement;
}

describe('DispatchControlsComponent', () => {
  it('should show "Pause All" when dispatch is active', () => {
    // Arrange / Act
    const { fixture } = setup();

    // Assert
    const btn = getToggleBtn(fixture);
    expect(btn.textContent?.trim()).toBe('Pause All');
  });

  it('should show "Resume All" when dispatch is paused', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: true });

    // Assert
    const btn = getToggleBtn(fixture);
    expect(btn.textContent?.trim()).toBe('Resume All');
  });

  it('should show "Resume All" when usage limit is active', () => {
    // Arrange
    const futureDate = new Date(Date.now() + 60_000).toISOString();

    // Act
    const { fixture } = setup({ usageLimitResetsAt: futureDate });

    // Assert
    const btn = getToggleBtn(fixture);
    expect(btn.textContent?.trim()).toBe('Resume All');
  });

  it('should call pauseDispatch when clicked in active state', () => {
    // Arrange
    const { fixture, mockDispatch } = setup();

    // Act
    getToggleBtn(fixture).click();

    // Assert
    expect(mockDispatch.pauseDispatch).toHaveBeenCalledTimes(1);
    expect(mockDispatch.resumeDispatch).not.toHaveBeenCalled();
  });

  it('should call resumeDispatch when clicked in paused state', () => {
    // Arrange
    const { fixture, mockDispatch } = setup({ isDispatchPaused: true });

    // Act
    getToggleBtn(fixture).click();

    // Assert
    expect(mockDispatch.resumeDispatch).toHaveBeenCalledTimes(1);
    expect(mockDispatch.pauseDispatch).not.toHaveBeenCalled();
  });

  it('should show "Pausing..." and disable while pausing', () => {
    // Arrange / Act
    const { fixture } = setup({ pausing: true });

    // Assert
    const btn = getToggleBtn(fixture);
    expect(btn.disabled).toBe(true);
    expect(btn.textContent?.trim()).toBe('Pausing...');
  });

  it('should show "Resuming..." and disable while resuming', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: true, resuming: true });

    // Assert
    const btn = getToggleBtn(fixture);
    expect(btn.disabled).toBe(true);
    expect(btn.textContent?.trim()).toBe('Resuming...');
  });

  it('should apply resume modifier class when paused', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: true });

    // Assert
    const btn = getToggleBtn(fixture);
    expect(btn.classList.contains('dispatch-controls__toggle-btn--resume')).toBe(true);
  });

  it('should not apply resume modifier class when active', () => {
    // Arrange / Act
    const { fixture } = setup();

    // Assert
    const btn = getToggleBtn(fixture);
    expect(btn.classList.contains('dispatch-controls__toggle-btn--resume')).toBe(false);
  });

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

  it('should show "Dispatch paused" status when manually paused', () => {
    // Arrange / Act
    const { fixture } = setup({ isDispatchPaused: true, usageLimitResetsAt: null });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const status = el.querySelector('.dispatch-controls__status') as HTMLElement;
    expect(status?.textContent?.trim()).toBe('Dispatch paused');
  });

  it('should show empty status text when dispatch is active', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const status = el.querySelector('.dispatch-controls__status') as HTMLElement;
    expect(status?.textContent?.trim()).toBe('');
  });

  it('should display error message when pauseResumeError is set', () => {
    // Arrange / Act
    const { fixture } = setup({ pauseResumeError: 'Failed to pause dispatch' });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const errorSpan = el.querySelector('.dispatch-controls__error');
    expect(errorSpan?.textContent?.trim()).toBe('Failed to pause dispatch');
  });

  it('should not display error span when pauseResumeError is null', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.dispatch-controls__error')).toBeNull();
  });

  it('should have role="group" and aria-label on the wrapper', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('.dispatch-controls') as HTMLElement;
    expect(wrapper?.getAttribute('role')).toBe('group');
    expect(wrapper?.getAttribute('aria-label')).toBe('Dispatch controls');
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
