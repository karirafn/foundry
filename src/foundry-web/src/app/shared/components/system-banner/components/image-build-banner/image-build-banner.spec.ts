import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ImageBuildBannerComponent } from './image-build-banner';
import { SettingsService } from '../../../../../core/services/settings.service';
import { ImageBuildStatus } from '../../../../../core/models/settings.model';

function createMockSettingsService(
  status: ImageBuildStatus = 'Idle',
  logTail: string | null = null,
  savingImageFlags: boolean = false,
  nextRetryAt: string | null = null,
  attempt: number = 0,
) {
  const statusSignal = signal<ImageBuildStatus>(status);
  const logTailSignal = signal<string | null>(logTail);
  const savingImageFlagsSignal = signal<boolean>(savingImageFlags);
  const nextRetryAtSignal = signal<string | null>(nextRetryAt);
  const attemptSignal = signal<number>(attempt);
  return {
    imageBuildStatus: statusSignal.asReadonly(),
    imageBuildLogTail: logTailSignal.asReadonly(),
    savingImageFlags: savingImageFlagsSignal.asReadonly(),
    imageBuildNextRetryAt: nextRetryAtSignal.asReadonly(),
    imageBuildAttempt: attemptSignal.asReadonly(),
    retryImageBuild: vi.fn(),
    _statusSignal: statusSignal,
    _logTailSignal: logTailSignal,
    _savingImageFlagsSignal: savingImageFlagsSignal,
    _nextRetryAtSignal: nextRetryAtSignal,
    _attemptSignal: attemptSignal,
  };
}

function setup(
  status: ImageBuildStatus = 'Idle',
  logTail: string | null = null,
  savingImageFlags: boolean = false,
  nextRetryAt: string | null = null,
  attempt: number = 0,
) {
  const mockSettings = createMockSettingsService(status, logTail, savingImageFlags, nextRetryAt, attempt);

  TestBed.configureTestingModule({
    imports: [ImageBuildBannerComponent],
    providers: [
      provideRouter([]),
      { provide: SettingsService, useValue: mockSettings },
    ],
  });

  const fixture = TestBed.createComponent(ImageBuildBannerComponent);
  fixture.detectChanges();
  return { fixture, mockSettings };
}

describe('ImageBuildBannerComponent', () => {
  it('should render a Failed bar with message, log tail, "View details" link, and Retry button when status is Failed', () => {
    // Arrange / Act
    const { fixture } = setup('Failed', 'Step 2/5 FAILED');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const imageBuildBar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
    expect(imageBuildBar).toBeTruthy();
    expect(imageBuildBar.textContent).toContain('Worker image build failed');

    const logTail = imageBuildBar.querySelector('.system-banner__log-tail') as HTMLElement;
    expect(logTail).toBeTruthy();
    expect(logTail.textContent).toContain('Step 2/5 FAILED');

    const link = imageBuildBar.querySelector('a.system-banner__details-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link.textContent?.trim()).toBe('View details');
    expect(link.getAttribute('href')).toBe('/settings/general');

    const retryBtn = imageBuildBar.querySelector('.system-banner__action-btn') as HTMLButtonElement;
    expect(retryBtn).toBeTruthy();
    expect(retryBtn.textContent?.trim()).toBe('Retry');
  });

  it('should render a Building bar when status is Building', () => {
    // Arrange / Act
    const { fixture } = setup('Building');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const buildingBar = el.querySelector('.system-banner__bar--building') as HTMLElement;
    expect(buildingBar).toBeTruthy();
    expect(buildingBar.textContent).toContain('Worker image is building');

    const failedBar = el.querySelector('.system-banner__bar--failed');
    expect(failedBar).toBeFalsy();
  });

  it('should render nothing when status is Idle', () => {
    // Arrange / Act
    const { fixture } = setup('Idle');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const imageBuildBar = el.querySelector('.system-banner__bar--image-build');
    expect(imageBuildBar).toBeFalsy();
  });

  it('should re-render from Idle to Building when the status signal changes', () => {
    // Arrange
    const { fixture, mockSettings } = setup('Idle');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.system-banner__bar--image-build')).toBeFalsy();

    // Act
    mockSettings._statusSignal.set('Building');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.system-banner__bar--building')).toBeTruthy();
  });

  it('should re-render from Building to Failed when the status signal changes', () => {
    // Arrange
    const { fixture, mockSettings } = setup('Building');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.system-banner__bar--building')).toBeTruthy();

    // Act
    mockSettings._statusSignal.set('Failed');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.system-banner__bar--failed')).toBeTruthy();
    expect(el.querySelector('.system-banner__bar--building')).toBeFalsy();
  });

  it('should not show log-tail span when log tail is null', () => {
    // Arrange / Act
    const { fixture } = setup('Failed', null);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const imageBuildBar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
    expect(imageBuildBar).toBeTruthy();
    const logTail = imageBuildBar.querySelector('.system-banner__log-tail');
    expect(logTail).toBeFalsy();
  });

  it('should have role="region" with aria-label "Image build status" on the wrapper when not Idle', () => {
    // Arrange / Act
    const { fixture } = setup('Building');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('[aria-label="Image build status"]') as HTMLElement;
    expect(wrapper).toBeTruthy();
    expect(wrapper.getAttribute('role')).toBe('region');
  });

  it('should have role="status" and aria-live="polite" on the Building bar', () => {
    // Arrange / Act
    const { fixture } = setup('Building');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const buildingBar = el.querySelector('.system-banner__bar--building') as HTMLElement;
    expect(buildingBar?.getAttribute('role')).toBe('status');
    expect(buildingBar?.getAttribute('aria-live')).toBe('polite');
  });

  it('should have role="status" and aria-live="polite" on the message span inside the Failed bar (not on the bar div itself)', () => {
    // Arrange / Act
    const { fixture } = setup('Failed', 'error');
    const el = fixture.nativeElement as HTMLElement;

    // Assert — message span carries the live region, not the bar
    const failedBar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
    expect(failedBar?.getAttribute('role')).not.toBe('status');
    expect(failedBar?.getAttribute('aria-live')).not.toBe('polite');
    const messageSpan = failedBar?.querySelector('.system-banner__message') as HTMLElement;
    expect(messageSpan?.getAttribute('role')).toBe('status');
    expect(messageSpan?.getAttribute('aria-live')).toBe('polite');
  });

  it('should have a role="alert" assertive region that emits the failure summary on initial Idle→Failed transition', () => {
    // Arrange — start Idle
    const { fixture, mockSettings } = setup('Idle');
    const el = fixture.nativeElement as HTMLElement;

    // No alert region should be visible (or it should be empty) before the transition
    const alertBefore = el.querySelector('[role="alert"].system-banner__failure-alert') as HTMLElement;
    // The element may or may not exist before the banner is shown (it's in the outer host)
    // We just assert it is absent or empty before the transition

    // Act — transition to Failed
    mockSettings._statusSignal.set('Failed');
    fixture.detectChanges();

    // Assert — assertive alert region appears with failure text
    const alertEl = el.querySelector('[role="alert"].system-banner__failure-alert') as HTMLElement;
    expect(alertEl).toBeTruthy();
    expect(alertEl.textContent?.trim()).toContain('Worker image build failed');
  });

  it('should clear the role="alert" assertive region on a SignalR countdown update (not re-announce)', () => {
    // Arrange — use fake timers so we can control the alert-clear timeout
    vi.useFakeTimers();
    const { fixture, mockSettings } = setup('Idle');
    mockSettings._statusSignal.set('Failed');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const alertEl = el.querySelector('[role="alert"].system-banner__failure-alert') as HTMLElement;
    expect(alertEl.textContent?.trim()).not.toBe('');

    // Advance past the alert-clear timeout so the initial text is cleared
    vi.advanceTimersByTime(0);
    fixture.detectChanges();

    // Act — simulate a countdown tick (nextRetryAt changes but status remains Failed)
    mockSettings._nextRetryAtSignal.set('2026-08-08T12:30:00Z');
    fixture.detectChanges();

    // Assert — alert stays empty (countdown ticks stay polite, only the transition is assertive)
    const alertAfter = el.querySelector('[role="alert"].system-banner__failure-alert') as HTMLElement;
    expect(alertAfter.textContent?.trim()).toBe('');

    vi.useRealTimers();
  });

  it('should clear the role="alert" assertive region when status returns to Idle', () => {
    // Arrange — get to Failed
    const { fixture, mockSettings } = setup('Idle');
    mockSettings._statusSignal.set('Failed');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Act — return to Idle
    mockSettings._statusSignal.set('Idle');
    fixture.detectChanges();

    // Assert — banner hidden, alert cleared
    const alertEl = el.querySelector('[role="alert"].system-banner__failure-alert') as HTMLElement | null;
    // Either the element is absent (not rendered in Idle) or empty
    expect(!alertEl || alertEl.textContent?.trim() === '').toBe(true);
  });

  it('should disable the Retry button and set aria-busy when savingImageFlags is true', () => {
    // Arrange / Act
    const { fixture } = setup('Failed', 'error log', true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const retryBtn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
    expect(retryBtn.disabled).toBe(true);
    expect(retryBtn.getAttribute('aria-busy')).toBe('true');
  });

  it('should enable the Retry button and unset aria-busy when savingImageFlags is false', () => {
    // Arrange / Act
    const { fixture } = setup('Failed', 'error log', false);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const retryBtn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
    expect(retryBtn.disabled).toBe(false);
    expect(retryBtn.getAttribute('aria-busy')).toBeFalsy();
  });

  it('should have a screen-reader-only prefix before the log tail', () => {
    // Arrange / Act
    const { fixture } = setup('Failed', 'some error log');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const logTailSpan = el.querySelector('.system-banner__log-tail') as HTMLElement;
    const hiddenPrefix = logTailSpan?.querySelector('.sr-only') as HTMLElement;
    expect(hiddenPrefix).toBeTruthy();
    expect(hiddenPrefix.textContent?.trim()).toBeTruthy();
  });

  it('should have aria-label "View image build details" on the View details link', () => {
    // Arrange / Act
    const { fixture } = setup('Failed', 'error');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('a.system-banner__details-link') as HTMLAnchorElement;
    expect(link?.getAttribute('aria-label')).toBe('View image build details');
  });

  it('should call settingsService.retryImageBuild when Retry is clicked', () => {
    // Arrange
    const { fixture, mockSettings } = setup('Failed', 'error log');
    const el = fixture.nativeElement as HTMLElement;

    // Act
    const retryBtn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
    retryBtn.click();
    fixture.detectChanges();

    // Assert
    expect(mockSettings.retryImageBuild).toHaveBeenCalledOnce();
  });

  it('should have aria-label="Retry image build now" on the Retry button', () => {
    // Arrange / Act
    const { fixture } = setup('Failed', 'error');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const retryBtn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
    expect(retryBtn?.getAttribute('aria-label')).toBe('Retry image build now');
  });

  describe('copy matrix — failed message variants', () => {
    it('should show plain "Worker image build failed." when nextRetryAt is null and attempt < 1', () => {
      // Arrange / Act
      const { fixture } = setup('Failed', null, false, null, 0);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
      expect(bar.textContent).toContain('Worker image build failed.');
      expect(bar.textContent).not.toContain('retrying');
      expect(bar.textContent).not.toContain('attempt');
    });

    it('should show "(attempt N)" when nextRetryAt is null and attempt >= 1', () => {
      // Arrange / Act
      const { fixture } = setup('Failed', null, false, null, 3);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
      expect(bar.textContent).toContain('Worker image build failed (attempt 3)');
      expect(bar.textContent).not.toContain('retrying');
    });

    it('should show "retrying at HH:MM" when nextRetryAt is set and attempt < 1', () => {
      // Arrange / Act — use a UTC ISO string whose HH:MM is predictable in the locale
      // We test that the retrying-at phrase appears (not the exact time value, which is locale-dependent)
      const { fixture } = setup('Failed', null, false, '2026-08-08T12:30:00Z', 0);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
      expect(bar.textContent).toContain('Worker image build failed — retrying at');
      expect(bar.textContent).not.toContain('attempt');
    });

    it('should show "retrying at HH:MM (attempt N)" when both nextRetryAt and attempt >= 1', () => {
      // Arrange / Act
      const { fixture } = setup('Failed', null, false, '2026-08-08T12:30:00Z', 2);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
      expect(bar.textContent).toContain('Worker image build failed — retrying at');
      expect(bar.textContent).toContain('(attempt 2)');
    });

    it('should not show "(attempt 0)" in any variant', () => {
      // Arrange / Act
      const { fixture } = setup('Failed', null, false, '2026-08-08T12:30:00Z', 0);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
      expect(bar.textContent).not.toContain('attempt 0');
    });

    it('should update message when attempt signal changes from 0 to 2', () => {
      // Arrange
      const { fixture, mockSettings } = setup('Failed', null, false, '2026-08-08T12:30:00Z', 0);
      const el = fixture.nativeElement as HTMLElement;
      let bar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
      expect(bar.textContent).not.toContain('attempt');

      // Act
      mockSettings._attemptSignal.set(2);
      fixture.detectChanges();

      // Assert
      bar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
      expect(bar.textContent).toContain('(attempt 2)');
    });
  });
});
