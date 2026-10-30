import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { NgModel } from '@angular/forms';
import { vi } from 'vitest';
import { SettingsService } from '../../../core/services/settings.service';
import {
  setupSettingsPage,
  flushSettings,
  API_KEY_RESPONSE,
} from '../testing/settings-page-harness';
import { SettingsWorkerImageComponent } from './settings-worker-image';

describe('SettingsWorkerImageComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  describe('Worker Image section', () => {
    it('should render the "Worker Image" section title', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const headings = Array.from(el.querySelectorAll('h2'));
      const imageHeading = headings.find(h => h.textContent?.trim() === 'Worker Image');

      // Assert
      expect(imageHeading).toBeTruthy();
    });

    it('should render four checkboxes for the image flags', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const checkboxes = imageForm?.querySelectorAll('input[type="checkbox"]');

      // Assert
      expect(checkboxes?.length).toBe(4);
    });

    it('should initialize checkboxes from loaded settings', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: true });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const dotnetCheckbox = el.querySelector('#installDotnet') as HTMLInputElement;
      const angularCheckbox = el.querySelector('#installAngular') as HTMLInputElement;

      // Assert
      const dotnetModel = fixture.debugElement.query(By.css('#installDotnet')).injector.get(NgModel);
      const angularModel = fixture.debugElement.query(By.css('#installAngular')).injector.get(NgModel);
      expect(dotnetModel.model).toBe(true);
      expect(angularModel.model).toBe(false);
      expect(dotnetCheckbox).toBeTruthy();
    });

    it('should disable the Save button when flags are not dirty', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: false });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;

      // Assert
      expect(saveBtn.disabled).toBe(true);
    });

    it('should enable the Save button when a flag has changed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: false });
      fixture.detectChanges();

      // Act — toggle installDotnet to make it dirty
      const component = fixture.componentInstance as unknown as { _installDotnetValue: { set: (v: boolean) => void } };
      component._installDotnetValue.set(true);
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      expect(saveBtn.disabled).toBe(false);
    });

    it('should call updateWorkerImageFlags when Save is clicked', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: false, installAngular: false });
      fixture.detectChanges();

      // Make dirty
      const component = fixture.componentInstance as unknown as { _installDotnetValue: { set: (v: boolean) => void } };
      component._installDotnetValue.set(true);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/worker-image');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ installDotnet: true, installAngular: false, installChromium: false, installDocker: false });
      req.flush({ ...API_KEY_RESPONSE, installDotnet: true });
    });

    it('should disable checkboxes and Save while imageBuildStatus is Building', () => {
      // Arrange
      const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Building' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const checkboxes = Array.from(imageForm?.querySelectorAll('input[type="checkbox"]') ?? []) as HTMLInputElement[];
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;

      // Assert
      expect(service.imageBuildStatus()).toBe('Building');
      checkboxes.forEach(cb => expect(cb.disabled).toBe(true));
      expect(saveBtn.disabled).toBe(true);
    });

    it('should show persistent role="status" region with building text when Building', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Building' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];

      // Assert — at least one polite region contains the building text
      const buildingRegion = statusRegions.find(r => r.textContent?.includes('Building worker image'));
      expect(buildingRegion).toBeTruthy();
      expect(buildingRegion?.getAttribute('aria-live')).toBe('polite');
    });

    it('should have a persistent role="status" image-failed region always in the DOM (polite, not assertive)', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusRegion = el.querySelector('.general-settings__image-status[role="status"][aria-live="polite"]') as HTMLElement;

      // Assert — the polite failed-text region exists (may be empty when Idle)
      expect(statusRegion).toBeTruthy();
    });

    it('should show failed text in role="status" (polite) region and build log pre element when Failed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'Step 2/5 FAILED', nextRetryAt: null, attempt: 0 });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
      const logPre = el.querySelector('.general-settings__image-log') as HTMLElement;

      // Assert — one of the status regions contains the failed text
      const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));
      expect(failedRegion).toBeTruthy();
      expect(logPre).toBeTruthy();
      expect(logPre.tagName).toBe('PRE');
      expect(logPre.getAttribute('tabindex')).toBe('0');
      expect(logPre.getAttribute('aria-label')).toBe('Build log output');
      expect(logPre.textContent).toContain('Step 2/5 FAILED');
    });

    it('should show Retry button when imageBuildStatus is Failed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'error' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const retryBtn = imageForm?.querySelector('.general-settings__retry-btn') as HTMLButtonElement;

      // Assert
      expect(retryBtn).toBeTruthy();
      expect(retryBtn.textContent?.trim()).toBe('Retry');
    });

    it('should not show Retry button when imageBuildStatus is Idle', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.general-settings__retry-btn');

      // Assert
      expect(retryBtn).toBeFalsy();
    });

    it('should call retryImageBuild when Retry button is clicked', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.general-settings__retry-btn') as HTMLButtonElement;
      retryBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/worker-image/retry');
      expect(req.request.method).toBe('POST');
      req.flush({ ...API_KEY_RESPONSE, imageBuildStatus: 'Building' });
    });

    it('should have labels with correct "for" attributes for each checkbox', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const dotnetLabel = el.querySelector('label[for="installDotnet"]');
      const angularLabel = el.querySelector('label[for="installAngular"]');
      const chromiumLabel = el.querySelector('label[for="installChromium"]');
      const dockerLabel = el.querySelector('label[for="installDocker"]');

      // Assert
      expect(dotnetLabel).toBeTruthy();
      expect(angularLabel).toBeTruthy();
      expect(chromiumLabel).toBeTruthy();
      expect(dockerLabel).toBeTruthy();
    });

    it('should initialize installChromium and installDocker checkboxes from loaded settings', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installChromium: true, installDocker: true });
      fixture.detectChanges();

      // Act
      const chromiumModel = fixture.debugElement.query(By.css('#installChromium')).injector.get(NgModel);
      const dockerModel = fixture.debugElement.query(By.css('#installDocker')).injector.get(NgModel);

      // Assert
      expect(chromiumModel.model).toBe(true);
      expect(dockerModel.model).toBe(true);
    });

    it('should include installChromium and installDocker in the save payload', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installChromium: false, installDocker: false });
      fixture.detectChanges();

      // Make dirty by toggling installChromium
      const component = fixture.componentInstance as unknown as { _installChromiumValue: { set: (v: boolean) => void } };
      component._installChromiumValue.set(true);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/worker-image');
      expect(req.request.body).toEqual({ installDotnet: false, installAngular: false, installChromium: true, installDocker: false });
      req.flush({ ...API_KEY_RESPONSE, installChromium: true });
    });

    it('should show error message when save image flags fails', () => {
      // Arrange
      const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Make dirty and attempt save
      service.updateWorkerImageFlags({ installDotnet: true, installAngular: false, installChromium: false, installDocker: false });
      httpMock.expectOne('/api/settings/worker-image').flush('Error', { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const errorEl = el.querySelector('#image-flags-error');

      // Assert
      expect(errorEl).toBeTruthy();
      expect(errorEl?.getAttribute('role')).toBe('alert');
      expect(errorEl?.textContent).toContain('Failed to save worker image settings');
    });

    it('should wrap the four checkboxes in a fieldset with a sr-only legend', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const fieldset = el.querySelector('fieldset.general-settings__image-fieldset') as HTMLFieldSetElement;
      const legend = fieldset?.querySelector('legend');
      const checkboxesInsideFieldset = fieldset?.querySelectorAll('input[type="checkbox"]');

      // Assert
      expect(fieldset).toBeTruthy();
      expect(legend?.classList.contains('sr-only')).toBe(true);
      expect(legend?.textContent?.trim()).toBe('Preinstalled toolchains');
      expect(checkboxesInsideFieldset?.length).toBe(4);
    });

    it('should show success message in role="status" region after a successful image flags save', () => {
      // Arrange
      const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      service.updateWorkerImageFlags({ installDotnet: true, installAngular: false, installChromium: false, installDocker: false });
      httpMock.expectOne('/api/settings/worker-image').flush({ ...API_KEY_RESPONSE, installDotnet: true, imageBuildStatus: 'Building' });
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const successEls = Array.from(el.querySelectorAll('[role="status"]'));
      const imageFlagsSuccess = successEls.find(e => e.textContent?.includes('Worker image settings saved'));
      expect(imageFlagsSuccess).toBeTruthy();
    });

    it('should route retry errors to the image-flags-error region', () => {
      // Arrange
      const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
      fixture.detectChanges();

      // Act
      service.retryImageBuild();
      httpMock.expectOne('/api/settings/worker-image/retry').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const errorEl = el.querySelector('#image-flags-error');
      expect(errorEl?.textContent).toContain('Failed to save worker image settings');
    });

    it('should have aria-label="Retry image build now" on the Retry button', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.general-settings__retry-btn') as HTMLButtonElement;

      // Assert
      expect(retryBtn?.getAttribute('aria-label')).toBe('Retry image build now');
    });

    it('should disable the Retry button while savingImageFlags is true', () => {
      // Arrange
      const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
      fixture.detectChanges();

      // Act — trigger a save so savingImageFlags becomes true
      service.retryImageBuild();
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.general-settings__retry-btn') as HTMLButtonElement;
      expect(retryBtn.disabled).toBe(true);

      httpMock.expectOne('/api/settings/worker-image/retry').flush({ ...API_KEY_RESPONSE, imageBuildStatus: 'Building', nextRetryAt: null, attempt: 0 });
    });

    describe('_imageFailedText copy matrix', () => {
      it('should show plain "Worker image build failed." when nextRetryAt is null and attempt is 0', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err', nextRetryAt: null, attempt: 0 });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
        const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));

        // Assert
        expect(failedRegion?.textContent?.trim()).toBe('Worker image build failed.');
      });

      it('should show "(attempt N)" when nextRetryAt is null and attempt >= 1', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', nextRetryAt: null, attempt: 2 });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
        const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));

        // Assert
        expect(failedRegion?.textContent).toContain('Worker image build failed (attempt 2)');
        expect(failedRegion?.textContent).not.toContain('retrying');
      });

      it('should show "retrying at HH:MM" when nextRetryAt is set and attempt < 1', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', nextRetryAt: '2026-08-08T12:30:00Z', attempt: 0 });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
        const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));

        // Assert
        expect(failedRegion?.textContent).toContain('Worker image build failed — retrying at');
        expect(failedRegion?.textContent).not.toContain('attempt');
      });

      it('should show "retrying at HH:MM (attempt N)" when both nextRetryAt is set and attempt >= 1', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', nextRetryAt: '2026-08-08T12:30:00Z', attempt: 3 });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
        const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));

        // Assert
        expect(failedRegion?.textContent).toContain('Worker image build failed — retrying at');
        expect(failedRegion?.textContent).toContain('(attempt 3)');
      });
    });

    describe('initial-failure assertive alert', () => {
      it('should have a visually-hidden role="alert" region that emits text on the initial Idle→Failed transition', () => {
        // Arrange — start Idle
        const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock);
        fixture.detectChanges();

        const el = fixture.nativeElement as HTMLElement;

        // Act — transition to Failed via loadSettings
        service.loadSettings();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
        fixture.detectChanges();

        // Assert — assertive alert fires with failure text
        const alertEl = el.querySelector('[role="alert"].general-settings__image-failure-alert') as HTMLElement;
        expect(alertEl).toBeTruthy();
        expect(alertEl.textContent?.trim()).toContain('Worker image build failed');
      });

      it('should NOT fire the assertive alert when status stays Failed (countdown tick)', () => {
        // Arrange — use fake timers to control the alert clear timeout
        vi.useFakeTimers();
        const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err', nextRetryAt: null, attempt: 0 });
        fixture.detectChanges();

        const el = fixture.nativeElement as HTMLElement;

        // Advance past the clear timeout
        vi.advanceTimersByTime(0);
        fixture.detectChanges();

        // Act — countdown tick (nextRetryAt changes but status stays Failed)
        service.loadSettings();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err', nextRetryAt: '2026-08-08T12:30:00Z', attempt: 1 });
        fixture.detectChanges();

        // Assert — alert is empty (no re-announcement)
        const alertEl = el.querySelector('[role="alert"].general-settings__image-failure-alert') as HTMLElement;
        expect(alertEl?.textContent?.trim()).toBe('');

        vi.useRealTimers();
      });

      it('should clear the assertive alert when status returns to Idle', () => {
        // Arrange — start Failed
        const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
        fixture.detectChanges();

        const el = fixture.nativeElement as HTMLElement;

        // Act — return to Idle
        service.loadSettings();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Idle', lastImageBuildError: null });
        fixture.detectChanges();

        // Assert — alert is empty
        const alertEl = el.querySelector('[role="alert"].general-settings__image-failure-alert') as HTMLElement | null;
        expect(!alertEl || alertEl.textContent?.trim() === '').toBe(true);
      });
    });
  });

  describe('installDocker hint', () => {
    it('should render a warning hint for the installDocker checkbox with security cost text', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const hint = el.querySelector('#installDocker-hint') as HTMLElement;

      // Assert
      expect(hint).toBeTruthy();
      expect(hint.textContent).toContain('CAP_SYS_ADMIN');
      expect(hint.textContent).toContain('seccomp/AppArmor unconfined');
      expect(hint.textContent).toContain('Docker host');
      expect(hint.textContent).toContain('Testcontainers');
      expect(hint.classList).toContain('general-settings__field-hint--warning');
    });
  });

  describe('UnsavedChanges', () => {
    it('should return false when flags are not dirty', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: false });
      fixture.detectChanges();

      // Act
      const result = fixture.componentInstance.hasUnsavedChanges();

      // Assert
      expect(result).toBe(false);
    });

    it('should return true when a flag has changed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: false });
      fixture.detectChanges();

      // Act
      const component = fixture.componentInstance as unknown as { _installDotnetValue: { set: (v: boolean) => void } };
      component._installDotnetValue.set(true);
      fixture.detectChanges();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should return false when workerImageFlags is null (not yet loaded)', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      // Do not flush settings — workerImageFlags stays null

      // Act
      const result = fixture.componentInstance.hasUnsavedChanges();

      // Assert
      expect(result).toBe(false);

      // Cleanup
      flushSettings(httpMock);
    });
  });
});
