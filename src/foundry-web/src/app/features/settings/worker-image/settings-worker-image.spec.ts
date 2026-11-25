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
import { WORKER_IMAGE_TOOLCHAIN_URL } from './worker-image-toolchain';

describe('SettingsWorkerImageComponent', () => {
  afterEach(() => {
    const httpMock = TestBed.inject(HttpTestingController);
    // The toolchain block fetches on its own; its specs live in worker-image-toolchain.spec.ts.
    httpMock.match(WORKER_IMAGE_TOOLCHAIN_URL);
    httpMock.verify();
  });

  describe('Installed toolchain block', () => {
    it('should not render the block before settings load', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);

      // Act
      fixture.detectChanges();

      // Assert
      expect((fixture.nativeElement as HTMLElement).querySelector('fd-worker-image-toolchain')).toBeNull();
      flushSettings(httpMock);
    });

    it('should place the block between the section description and the image form', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();

      // Act
      flushSettings(httpMock);
      fixture.detectChanges();

      // Assert
      const block = (fixture.nativeElement as HTMLElement).querySelector('fd-worker-image-toolchain')!;
      expect(block.previousElementSibling?.classList.contains('general-settings__section-description')).toBe(true);
      expect(block.nextElementSibling?.classList.contains('general-settings__image-form')).toBe(true);
    });
  });

  describe('Worker Image section', () => {
    it('should render the image tag input with id "imageTag"', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('#imageTag') as HTMLInputElement;

      // Assert
      expect(input).toBeTruthy();
      expect(input.type).toBe('text');
    });

    it('should render the build enabled checkbox with id "buildEnabled"', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const checkbox = el.querySelector('#buildEnabled') as HTMLInputElement;

      // Assert
      expect(checkbox).toBeTruthy();
      expect(checkbox.type).toBe('checkbox');
    });

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

    it('should render no toolchain install toggles', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const checkboxIds = Array.from(imageForm.querySelectorAll('input[type="checkbox"]')).map(cb => cb.id);

      // Assert
      expect(checkboxIds).toEqual(['buildEnabled']);
      expect(el.textContent).not.toContain('Preinstalled toolchains');
    });

    it('should initialize imageTag input from loaded settings', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:2.1.0' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('#imageTag') as HTMLInputElement;
      const ngModel = fixture.debugElement.query(By.css('#imageTag')).injector.get(NgModel);

      // Assert
      expect(ngModel.model).toBe('foundry-worker:2.1.0');
      expect(input).toBeTruthy();
    });

    it('should initialize buildEnabled checkbox from loaded settings', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, buildEnabled: false });
      fixture.detectChanges();

      // Act
      const ngModel = fixture.debugElement.query(By.css('#buildEnabled')).injector.get(NgModel);

      // Assert
      expect(ngModel.model).toBe(false);
    });

    it('should mark dirty when imageTag is changed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
      fixture.detectChanges();

      // Act
      const component = fixture.componentInstance as unknown as { _imageTagValue: { set: (v: string) => void } };
      component._imageTagValue.set('foundry-worker:2.0.0');
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const saveBtn = el.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      expect(saveBtn.disabled).toBe(false);
    });

    it('should mark dirty when buildEnabled is toggled', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, buildEnabled: true });
      fixture.detectChanges();

      // Act
      const component = fixture.componentInstance as unknown as { _buildEnabledValue: { set: (v: boolean) => void } };
      component._buildEnabledValue.set(false);
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const saveBtn = el.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      expect(saveBtn.disabled).toBe(false);
    });

    it('should disable the Save button when flags are not dirty', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;

      // Assert
      expect(saveBtn.disabled).toBe(true);
    });

    it('should trim the imageTag value before sending in the save payload', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
      fixture.detectChanges();

      // Make dirty by setting a tag with surrounding whitespace
      const component = fixture.componentInstance as unknown as { _imageTagValue: { set: (v: string) => void } };
      component._imageTagValue.set('  foundry-worker:2.0.0  ');
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const saveBtn = el.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert — trimmed in the payload
      const req = httpMock.expectOne('/api/settings/worker-image');
      expect(req.request.body.imageTag).toBe('foundry-worker:2.0.0');
      req.flush({ ...API_KEY_RESPONSE, imageTag: 'foundry-worker:2.0.0' });
    });

    it('should call updateWorkerImageFlags when Save is clicked', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE });
      fixture.detectChanges();

      // Make dirty
      const component = fixture.componentInstance as unknown as { _buildEnabledValue: { set: (v: boolean) => void } };
      component._buildEnabledValue.set(false);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/worker-image');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ imageTag: 'foundry-worker:local', buildEnabled: false });
      req.flush({ ...API_KEY_RESPONSE });
    });

    it('should show explanatory warning note when buildEnabled is false', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, buildEnabled: false });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const note = el.querySelector('#buildEnabled-off-note') as HTMLElement;

      // Assert
      expect(note).toBeTruthy();
      expect(note.textContent).toContain('Foundry never pulls images');
      expect(note.classList).toContain('general-settings__callout--warning');
      expect(note.classList).not.toContain('general-settings__field-hint--warning');
      expect(note.querySelector('.general-settings__callout-lead')?.textContent?.trim()).toBe('Warning:');
      expect(note.hidden).toBe(false);
    });

    it('should keep #buildEnabled-off-note in the DOM but hidden when buildEnabled is true', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, buildEnabled: true });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const note = el.querySelector('#buildEnabled-off-note') as HTMLElement | null;

      // Assert — element is present but hidden (fix 2: [hidden] instead of @if)
      expect(note).toBeTruthy();
      expect(note?.hidden).toBe(true);
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

    it('should show error message when save image flags fails', () => {
      // Arrange
      const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

      // Make dirty and attempt save
      service.updateWorkerImageFlags({ imageTag: 'foundry-worker:local', buildEnabled: true });
      httpMock.expectOne('/api/settings/worker-image').flush('Error', { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const errorEl = el.querySelector('#image-flags-error');

      // Assert
      expect(errorEl).toBeTruthy();
      expect(errorEl?.getAttribute('role')).toBe('alert');
      expect(errorEl?.textContent).toContain('Failed to save worker image settings');
      expect(consoleError).toHaveBeenCalledWith(
        expect.objectContaining({ status: 500, url: '/api/settings/worker-image' })
      );
    });

    describe('image tag validation', () => {
      it('should show "Image tag is required." error when tag is empty after interaction', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
        fixture.detectChanges();

        // Act — simulate user interaction then clear the tag
        const input = fixture.nativeElement.querySelector('#imageTag') as HTMLInputElement;
        input.value = '';
        input.dispatchEvent(new Event('input'));
        fixture.detectChanges();

        // Assert
        const el = fixture.nativeElement as HTMLElement;
        const errorEl = el.querySelector('#imageTag-error') as HTMLElement;
        expect(errorEl.textContent).toContain('Image tag is required.');
      });

      it('should show ":latest not allowed" error when tag ends in :latest after interaction', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
        fixture.detectChanges();

        // Act — simulate user interaction then set :latest tag
        const input = fixture.nativeElement.querySelector('#imageTag') as HTMLInputElement;
        input.value = 'foundry-worker:latest';
        input.dispatchEvent(new Event('input'));
        fixture.detectChanges();

        // Assert
        const el = fixture.nativeElement as HTMLElement;
        const errorEl = el.querySelector('#imageTag-error') as HTMLElement;
        expect(errorEl.textContent).toContain(":latest' is not allowed");
      });

      it('should treat :LATEST (uppercase) as invalid (case-insensitive) after interaction', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
        fixture.detectChanges();

        // Act — simulate user interaction
        const input = fixture.nativeElement.querySelector('#imageTag') as HTMLInputElement;
        input.value = 'foundry-worker:LATEST';
        input.dispatchEvent(new Event('input'));
        fixture.detectChanges();

        // Assert — validation error shown (mirrors server's OrdinalIgnoreCase)
        const el = fixture.nativeElement as HTMLElement;
        const errorEl = el.querySelector('#imageTag-error') as HTMLElement;
        expect(errorEl.textContent).toContain(":latest' is not allowed");
      });

      it('should disable the Save button when tag is empty even if dirty', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
        fixture.detectChanges();

        // Act — clear the tag (makes dirty AND invalid)
        const component = fixture.componentInstance as unknown as { _imageTagValue: { set: (v: string) => void } };
        component._imageTagValue.set('');
        fixture.detectChanges();

        // Assert
        const el = fixture.nativeElement as HTMLElement;
        const saveBtn = el.querySelector('.general-settings__save-btn') as HTMLButtonElement;
        expect(saveBtn.disabled).toBe(true);
      });

      it('should not show validation error when tag is valid', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:1.2.3' });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const errorEl = el.querySelector('#imageTag-error') as HTMLElement;

        // Assert — no error text
        expect(errorEl.textContent?.trim()).toBe('');
      });
    });

    it('should show success message in role="status" region after a successful image flags save', () => {
      // Arrange
      const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      service.updateWorkerImageFlags({ imageTag: 'foundry-worker:local', buildEnabled: true });
      httpMock.expectOne('/api/settings/worker-image').flush({ ...API_KEY_RESPONSE, imageBuildStatus: 'Building' });
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
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

      // Act
      service.retryImageBuild();
      httpMock.expectOne('/api/settings/worker-image/retry').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const errorEl = el.querySelector('#image-flags-error');
      expect(errorEl?.textContent).toContain('Failed to save worker image settings');
      expect(consoleError).toHaveBeenCalledWith(
        expect.objectContaining({ status: 500, url: '/api/settings/worker-image/retry' })
      );
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

    describe('fix 1 — no error announced before interaction', () => {
      it('should not set aria-invalid on imageTag before user interacts', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
        fixture.detectChanges();

        // Act — no interaction yet
        const el = fixture.nativeElement as HTMLElement;
        const input = el.querySelector('#imageTag') as HTMLInputElement;

        // Assert
        expect(input.getAttribute('aria-invalid')).toBeNull();
      });

      it('should not render error text before user interacts, even if tag would be invalid', () => {
        // Arrange — settings load with a valid tag; we then set an empty tag without interaction flag
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
        fixture.detectChanges();

        // Act — no interaction yet (tag is valid so error is empty anyway; check initial render with empty tag)
        // Simulate page load with an empty imageTag in settings (edge case)
        const { fixture: f2, httpMock: h2 } = setupSettingsPage(SettingsWorkerImageComponent);
        f2.detectChanges();
        flushSettings(h2, { ...API_KEY_RESPONSE, imageTag: '' });
        f2.detectChanges();

        const el2 = f2.nativeElement as HTMLElement;
        const errorEl = el2.querySelector('#imageTag-error') as HTMLElement;
        const input2 = el2.querySelector('#imageTag') as HTMLInputElement;

        // Assert — no error shown before interaction even though tag is empty
        expect(errorEl.textContent?.trim()).toBe('');
        expect(input2.getAttribute('aria-invalid')).toBeNull();

        h2.match(WORKER_IMAGE_TOOLCHAIN_URL);
        h2.verify();
      });

      it('should set aria-invalid and show error after the user interacts with imageTag', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
        fixture.detectChanges();

        // Act — simulate ngModelChange (user types)
        const input = fixture.nativeElement.querySelector('#imageTag') as HTMLInputElement;
        input.value = '';
        input.dispatchEvent(new Event('input'));
        fixture.detectChanges();

        const el = fixture.nativeElement as HTMLElement;
        const errorEl = el.querySelector('#imageTag-error') as HTMLElement;

        // Assert — error is now visible and aria-invalid is set
        expect(errorEl.textContent).toContain('Image tag is required.');
        expect(input.getAttribute('aria-invalid')).toBe('true');
      });

      it('should still block Save even before interaction when tag is invalid', () => {
        // Arrange — load with empty tag (invalid) but no interaction
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
        fixture.detectChanges();

        // Make the tag invalid without using the input event (direct signal set)
        const component = fixture.componentInstance as unknown as { _imageTagValue: { set: (v: string) => void } };
        component._imageTagValue.set('');
        fixture.detectChanges();

        // Assert — Save is still blocked
        const el = fixture.nativeElement as HTMLElement;
        const saveBtn = el.querySelector('.general-settings__save-btn') as HTMLButtonElement;
        expect(saveBtn.disabled).toBe(true);
      });
    });

    describe('fix 2 — off-note always in DOM', () => {
      it('should keep #buildEnabled-off-note always in DOM, hidden when build is enabled', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, buildEnabled: true });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const note = el.querySelector('#buildEnabled-off-note') as HTMLElement;

        // Assert
        expect(note).toBeTruthy();
        expect(note.hidden).toBe(true);
      });

      it('should show #buildEnabled-off-note (not hidden) when build is disabled', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, buildEnabled: false });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const note = el.querySelector('#buildEnabled-off-note') as HTMLElement;

        // Assert
        expect(note).toBeTruthy();
        expect(note.hidden).toBe(false);
      });

      it('should reference buildEnabled-off-note in aria-describedby statically', () => {
        // Arrange — test with build ON (note is hidden but referenced)
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, buildEnabled: true });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const checkbox = el.querySelector('#buildEnabled') as HTMLInputElement;
        const describedBy = checkbox.getAttribute('aria-describedby') ?? '';

        // Assert — static reference present regardless of toggle state
        expect(describedBy).toContain('buildEnabled-off-note');
      });
    });

    describe('fix 3 — toggle disabled during save', () => {
      it('should disable buildEnabled checkbox while savingImageFlags is true', () => {
        // Arrange
        const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE });
        fixture.detectChanges();

        // Act — trigger a save so savingImageFlags becomes true mid-request
        service.updateWorkerImageFlags({ imageTag: 'foundry-worker:local', buildEnabled: true });
        fixture.detectChanges();

        const el = fixture.nativeElement as HTMLElement;
        const buildCheckbox = el.querySelector('#buildEnabled') as HTMLInputElement;

        // Assert — disabled during save
        expect(buildCheckbox.disabled).toBe(true);

        // Cleanup
        httpMock.expectOne('/api/settings/worker-image').flush({ ...API_KEY_RESPONSE });
      });

    });

    describe('fix 4 — build toggle hint', () => {
      it('should render a static #buildEnabled-hint span with descriptive copy', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock);
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const hint = el.querySelector('#buildEnabled-hint') as HTMLElement;

        // Assert
        expect(hint).toBeTruthy();
        expect(hint.textContent).toContain('stops Foundry building the image');
      });

      it('should include buildEnabled-hint in aria-describedby of the buildEnabled checkbox', () => {
        // Arrange
        const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
        fixture.detectChanges();
        flushSettings(httpMock);
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const checkbox = el.querySelector('#buildEnabled') as HTMLInputElement;
        const describedBy = checkbox.getAttribute('aria-describedby') ?? '';

        // Assert
        expect(describedBy).toContain('buildEnabled-hint');
      });
    });

  describe('Nested Docker switch', () => {
    const NESTED_DOCKER_URL = '/api/settings/nested-docker';

    function saveNestedDockerButton(el: HTMLElement): HTMLButtonElement {
      return el.querySelector('button[aria-label="Save nested Docker setting"]') as HTMLButtonElement;
    }

    it('should render the switch with its label and hint', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const checkbox = el.querySelector('#nestedDockerEnabled') as HTMLInputElement;
      const label = el.querySelector('label[for="nestedDockerEnabled"]') as HTMLLabelElement;
      const hint = el.querySelector('#nestedDockerEnabled-hint') as HTMLElement;

      // Assert
      expect(checkbox.type).toBe('checkbox');
      expect(label.textContent?.trim()).toBe('Nested Docker for implement runs');
      expect(hint.textContent?.trim()).toBe(
        'Runs Testcontainers integration tests inside implement workers through a nested rootless Docker daemon.'
      );
    });

    it('should render an always-visible security-cost callout beside the switch', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, nestedDockerEnabled: false });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const callout = el.querySelector('#nestedDockerEnabled-warning') as HTMLElement;

      // Assert
      expect(callout.hidden).toBe(false);
      expect(callout.classList).toContain('general-settings__callout--warning');
      expect(callout.querySelector('.general-settings__callout-lead')?.textContent?.trim()).toBe('Security cost:');
      expect(callout.querySelector('.general-settings__callout-icon')?.getAttribute('aria-hidden')).toBe('true');
      expect(callout.textContent?.replace(/\s+/g, ' ').trim()).toBe(
        '⚠Security cost: implement workers run with CAP_SYS_ADMIN and seccomp/AppArmor unconfined, so a privilege ' +
          'escalation inside a worker can reach the Docker host (the WSL VM on Docker Desktop). Applies to implement ' +
          'runs in every repository; triage runs never get it. Enable only if you trust all monitored repositories.'
      );
    });

    it('should reference the hint, the callout and the error region from the switch', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const describedBy = (el.querySelector('#nestedDockerEnabled') as HTMLInputElement).getAttribute('aria-describedby') ?? '';

      // Assert
      expect(describedBy.split(' ')).toEqual(['nestedDockerEnabled-hint', 'nestedDockerEnabled-warning', 'nested-docker-error']);
    });

    it('should initialize the switch from loaded settings', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, nestedDockerEnabled: true });
      fixture.detectChanges();

      // Act
      const ngModel = fixture.debugElement.query(By.css('#nestedDockerEnabled')).injector.get(NgModel);

      // Assert
      expect(ngModel.model).toBe(true);
    });

    it('should disable its Save button until the switch changes', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, nestedDockerEnabled: false });
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      const disabledBefore = saveNestedDockerButton(el).disabled;

      // Act
      const component = fixture.componentInstance as unknown as { _nestedDockerValue: { set: (v: boolean) => void } };
      component._nestedDockerValue.set(true);
      fixture.detectChanges();

      // Assert
      expect(disabledBefore).toBe(true);
      expect(saveNestedDockerButton(el).disabled).toBe(false);
    });

    it('should PUT only the switch to its own endpoint when its Save is clicked', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, nestedDockerEnabled: false });
      fixture.detectChanges();
      const component = fixture.componentInstance as unknown as { _nestedDockerValue: { set: (v: boolean) => void } };
      component._nestedDockerValue.set(true);
      fixture.detectChanges();

      // Act
      saveNestedDockerButton(fixture.nativeElement as HTMLElement).click();

      // Assert
      httpMock.expectNone('/api/settings/worker-image');
      const req = httpMock.expectOne(NESTED_DOCKER_URL);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ enabled: true });
      req.flush({ ...API_KEY_RESPONSE, nestedDockerEnabled: true });
    });

    it('should show its own saved message and not the image rebuild status after saving', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, nestedDockerEnabled: false });
      fixture.detectChanges();
      const component = fixture.componentInstance as unknown as { _nestedDockerValue: { set: (v: boolean) => void } };
      component._nestedDockerValue.set(true);
      fixture.detectChanges();

      // Act
      saveNestedDockerButton(fixture.nativeElement as HTMLElement).click();
      httpMock.expectOne(NESTED_DOCKER_URL).flush({ ...API_KEY_RESPONSE, nestedDockerEnabled: true });
      fixture.detectChanges();

      // Assert
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Nested Docker setting saved — applies to runs dispatched from now on.');
      expect(text).not.toContain('Worker image settings saved');
      expect(text).not.toContain('Building worker image');
    });

    it('should show its own error when saving the switch fails', () => {
      // Arrange
      const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

      // Act
      service.updateNestedDocker(true);
      httpMock.expectOne(NESTED_DOCKER_URL).flush('Error', { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const errorEl = el.querySelector('#nested-docker-error') as HTMLElement;
      expect(errorEl.getAttribute('role')).toBe('alert');
      expect(errorEl.textContent).toContain('Failed to save the nested Docker setting');
      expect(el.querySelector('#image-flags-error')?.textContent?.trim()).toBe('');
      expect(consoleError).toHaveBeenCalledWith(
        expect.objectContaining({ status: 500, url: NESTED_DOCKER_URL })
      );
    });

    it('should keep the switch usable while the image builds and when building is off', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, buildEnabled: false, imageBuildStatus: 'Building', nestedDockerEnabled: false });
      fixture.detectChanges();

      // Act
      const component = fixture.componentInstance as unknown as { _nestedDockerValue: { set: (v: boolean) => void } };
      component._nestedDockerValue.set(true);
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      expect((el.querySelector('#nestedDockerEnabled') as HTMLInputElement).disabled).toBe(false);
      expect(saveNestedDockerButton(el).disabled).toBe(false);
    });

    it('should disable the switch while its save is in flight', () => {
      // Arrange
      const { fixture, httpMock, service } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      service.updateNestedDocker(true);
      fixture.detectChanges();

      // Assert
      expect(((fixture.nativeElement as HTMLElement).querySelector('#nestedDockerEnabled') as HTMLInputElement).disabled).toBe(true);
      httpMock.expectOne(NESTED_DOCKER_URL).flush({ ...API_KEY_RESPONSE, nestedDockerEnabled: true });
    });
  });

  describe('toggle layout', () => {
    it('should render both checkboxes in the shared toggle row with the hint stacked underneath', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const ids = ['buildEnabled', 'nestedDockerEnabled'];

      // Assert
      for (const id of ids) {
        const checkbox = el.querySelector(`#${id}`) as HTMLInputElement;
        const row = checkbox.closest('label.general-settings__toggle-row') as HTMLLabelElement;
        expect(row).toBeTruthy();
        expect(row.htmlFor).toBe(id);
        const toggle = row.parentElement as HTMLElement;
        expect(toggle.classList).toContain('general-settings__toggle');
        const hint = toggle.querySelector(`#${id}-hint`) as HTMLElement;
        expect(hint.classList).toContain('general-settings__toggle-hint');
        expect(checkbox.getAttribute('aria-describedby')?.split(' ')).toContain(`${id}-hint`);
      }
      expect(el.querySelector('.general-settings__checkbox-label')).toBeNull();
      expect(el.querySelector('.general-settings__field-hint--warning')).toBeNull();
    });

    it('should use the shortened build-on-change hint copy', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const hint = (fixture.nativeElement as HTMLElement).querySelector('#buildEnabled-hint') as HTMLElement;

      // Assert
      expect(hint.textContent?.trim()).toBe(
        'Turning this off stops Foundry building the image; Foundry runs the tag above as it is.'
      );
    });
  });

  describe('feedback wrapper', () => {
    it('should group the four status regions in one feedback wrapper kept in the DOM', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const wrapper = imageForm.querySelector('.general-settings__image-feedback') as HTMLElement;

      // Assert
      expect(wrapper).toBeTruthy();
      expect(wrapper.querySelectorAll('.general-settings__image-status[role="status"]').length).toBe(2);
      expect(wrapper.querySelector('#image-flags-error[role="alert"]')).toBeTruthy();
      expect(wrapper.querySelector('.general-settings__save-success[role="status"]')).toBeTruthy();
      expect(imageForm.querySelectorAll('.general-settings__image-feedback').length).toBe(1);
    });

    it('should render the build log below the feedback wrapper when the build failed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'step 3 failed' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const wrapper = el.querySelector('.general-settings__image-feedback') as HTMLElement;
      const log = el.querySelector('.general-settings__image-log') as HTMLElement;

      // Assert
      expect(log).toBeTruthy();
      expect(wrapper.contains(log)).toBe(false);
      expect(wrapper.compareDocumentPosition(log) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });
  });

  describe('UnsavedChanges', () => {
    it('should return false when flags are not dirty', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE });
      fixture.detectChanges();

      // Act
      const result = fixture.componentInstance.hasUnsavedChanges();

      // Assert
      expect(result).toBe(false);
    });

    it('should return true when the nested Docker switch has changed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE });
      fixture.detectChanges();

      // Act
      const component = fixture.componentInstance as unknown as { _nestedDockerValue: { set: (v: boolean) => void } };
      component._nestedDockerValue.set(true);
      fixture.detectChanges();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should return true when imageTag has changed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageTag: 'foundry-worker:local' });
      fixture.detectChanges();

      // Act
      const component = fixture.componentInstance as unknown as { _imageTagValue: { set: (v: string) => void } };
      component._imageTagValue.set('foundry-worker:2.0.0');
      fixture.detectChanges();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should return true when buildEnabled has changed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsWorkerImageComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, buildEnabled: true });
      fixture.detectChanges();

      // Act
      const component = fixture.componentInstance as unknown as { _buildEnabledValue: { set: (v: boolean) => void } };
      component._buildEnabledValue.set(false);
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
