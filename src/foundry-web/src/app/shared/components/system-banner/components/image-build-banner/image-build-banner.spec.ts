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
  savingImageFlags: boolean = false
) {
  const statusSignal = signal<ImageBuildStatus>(status);
  const logTailSignal = signal<string | null>(logTail);
  const savingImageFlagsSignal = signal<boolean>(savingImageFlags);
  return {
    imageBuildStatus: statusSignal.asReadonly(),
    imageBuildLogTail: logTailSignal.asReadonly(),
    savingImageFlags: savingImageFlagsSignal.asReadonly(),
    retryImageBuild: vi.fn(),
    _statusSignal: statusSignal,
    _logTailSignal: logTailSignal,
    _savingImageFlagsSignal: savingImageFlagsSignal,
  };
}

function setup(status: ImageBuildStatus = 'Idle', logTail: string | null = null, savingImageFlags: boolean = false) {
  const mockSettings = createMockSettingsService(status, logTail, savingImageFlags);

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

  it('should have role="status" and aria-live="polite" on the Failed bar', () => {
    // Arrange / Act
    const { fixture } = setup('Failed', 'error');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const failedBar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
    expect(failedBar?.getAttribute('role')).toBe('status');
    expect(failedBar?.getAttribute('aria-live')).toBe('polite');
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
});
