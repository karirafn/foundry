import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { NgModel } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import {
  setupSettingsPage,
  flushSettings,
  API_KEY_RESPONSE,
} from '../testing/settings-page-harness';
import { SettingsProviderHostsComponent } from './settings-provider-hosts';

describe('SettingsProviderHostsComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  describe('Provider Hosts section', () => {
    it('should render the "Provider Hosts" section title', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const headings = Array.from(el.querySelectorAll('h2'));
      const hostsHeading = headings.find(h => h.textContent?.trim() === 'Provider Hosts');

      // Assert
      expect(hostsHeading).toBeTruthy();
    });

    it('should render the allowedProviderHosts textarea with id="allowedProviderHosts"', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const textarea = el.querySelector('#allowedProviderHosts') as HTMLTextAreaElement;

      // Assert
      expect(textarea).toBeTruthy();
      expect(textarea.tagName).toBe('TEXTAREA');
    });

    it('should seed the textarea with allowedProviderHosts joined by newlines', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com', 'gitlab.internal.corp'] });
      fixture.detectChanges();

      // Act
      const hostsNgModel = fixture.debugElement.query(By.css('#allowedProviderHosts')).injector.get(NgModel);

      // Assert
      expect(hostsNgModel.model).toBe('git.example.com\ngitlab.internal.corp');
    });

    it('should seed the textarea with empty string when allowedProviderHosts is empty', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act
      const hostsNgModel = fixture.debugElement.query(By.css('#allowedProviderHosts')).injector.get(NgModel);

      // Assert
      expect(hostsNgModel.model).toBe('');
    });

    it('should call updateAllowedProviderHosts with parsed array when Save is clicked', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act — set textarea value and click Save
      const component = fixture.componentInstance as unknown as { _allowedHostsValue: { set: (v: string) => void } };
      component._allowedHostsValue.set('git.example.com\ngitlab.internal.corp');
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const hostsForm = el.querySelector('.general-settings__hosts-form') as HTMLElement;
      const saveBtn = hostsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/allowed-provider-hosts');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ hosts: ['git.example.com', 'gitlab.internal.corp'] });
      req.flush({ ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com', 'gitlab.internal.corp'] });
    });

    it('should split newline and comma-separated input, trim, and drop blanks', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act
      const component = fixture.componentInstance as unknown as { _allowedHostsValue: { set: (v: string) => void } };
      component._allowedHostsValue.set('  git.example.com , \n  gitlab.internal.corp  \n\n  ');
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const hostsForm = el.querySelector('.general-settings__hosts-form') as HTMLElement;
      const saveBtn = hostsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/allowed-provider-hosts');
      expect(req.request.body).toEqual({ hosts: ['git.example.com', 'gitlab.internal.corp'] });
      req.flush({ ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com', 'gitlab.internal.corp'] });
    });

    it('should disable the Save button while savingHosts() is true', () => {
      // Arrange
      const { fixture, service, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act — trigger a save to put service in saving state
      service.updateAllowedProviderHosts(['git.example.com']);
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const hostsForm = el.querySelector('.general-settings__hosts-form') as HTMLElement;
      const saveBtn = hostsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      expect(saveBtn.disabled).toBe(true);
      expect(saveBtn.textContent?.trim()).toBe('Saving...');

      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush({ ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com'] });
    });

    it('should render saveHostsError() text in the error region', () => {
      // Arrange
      const { fixture, service, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act — trigger a failed save
      service.updateAllowedProviderHosts(['bad host!']);
      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush(
        { type: 'tag:foundry,2026:problems/Settings.InvalidHost', title: 'Bad Request', status: 400, detail: 'Host "bad host!" is invalid' },
        { status: 400, statusText: 'Bad Request' }
      );
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const errorEl = el.querySelector('#allowed-provider-hosts-error') as HTMLElement;
      expect(errorEl).toBeTruthy();
      expect(errorEl.getAttribute('role')).toBe('alert');
      expect(errorEl.textContent).toContain('bad host!');
    });

    it('should render success message in aria-live="polite" region after a successful hosts save', () => {
      // Arrange
      const { fixture, service, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act
      service.updateAllowedProviderHosts(['git.example.com']);
      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush({ ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com'] });
      fixture.detectChanges();

      // Assert — success region uses role="status" matching the established sibling pattern
      const el = fixture.nativeElement as HTMLElement;
      const hostsForm = el.querySelector('.general-settings__hosts-form') as HTMLElement;
      const successEl = hostsForm.querySelector('[role="status"].general-settings__save-success') as HTMLElement;
      expect(successEl).toBeTruthy();
      expect(successEl.textContent).toContain('Provider hosts saved successfully');
    });

    it('should set aria-invalid on the allowedProviderHosts textarea when saveHostsError is present', () => {
      // Arrange
      const { fixture, service, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act — trigger a failed save
      service.updateAllowedProviderHosts(['bad host!']);
      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush('Host "bad host!" is invalid', {
        status: 400,
        statusText: 'Bad Request',
      });
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const textarea = el.querySelector('#allowedProviderHosts') as HTMLTextAreaElement;
      expect(textarea.getAttribute('aria-invalid')).toBe('true');
    });

    it('should not set aria-invalid on the allowedProviderHosts textarea when there is no error', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Assert — no error, attribute must be absent
      const el = fixture.nativeElement as HTMLElement;
      const textarea = el.querySelector('#allowedProviderHosts') as HTMLTextAreaElement;
      expect(textarea.getAttribute('aria-invalid')).toBeNull();
    });

    it('should display hint text mentioning comma-separated input and 50-hostname cap', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const hint = el.querySelector('#allowed-provider-hosts-hint') as HTMLElement;

      // Assert
      expect(hint).toBeTruthy();
      expect(hint.textContent).toContain('comma-separated');
      expect(hint.textContent).toContain('Maximum 50 hostnames');
    });
  });

  describe('Provider Rate Budget section', () => {
    it('should render the "Provider Rate Budget" section title', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const titles = Array.from(el.querySelectorAll('.general-settings__section-title'));
      const rateBudgetTitle = titles.find((t) => t.textContent?.includes('Provider Rate Budget'));

      // Assert
      expect(rateBudgetTitle).toBeTruthy();
    });

    it('should render the fd-rate-budget-panel component', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const panel = el.querySelector('fd-rate-budget-panel');

      // Assert
      expect(panel).toBeTruthy();
    });
  });

  describe('UnsavedChanges guard', () => {
    it('should report hasUnsavedChanges() as false when allowedProviderHosts matches saved values', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com'] });
      fixture.detectChanges();

      // Assert — textarea is seeded from saved value, so not dirty
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });

    it('should report hasUnsavedChanges() as true when _allowedHostsValue differs from saved value', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com'] });
      fixture.detectChanges();

      // Act — change the textarea value
      const component = fixture.componentInstance as unknown as { _allowedHostsValue: { set: (v: string) => void } };
      component._allowedHostsValue.set('different.host.com');
      fixture.detectChanges();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should report hasUnsavedChanges() as false when saved state is null (no settings loaded yet)', () => {
      // Arrange — settings requests are in-flight but not yet resolved
      const { fixture, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();

      // Assert — null saved state is treated as NOT dirty
      const result = fixture.componentInstance.hasUnsavedChanges();
      expect(result).toBe(false);

      // Flush pending requests before afterEach.verify() runs
      flushSettings(httpMock);
      fixture.detectChanges();
    });

    it('should report hasUnsavedChanges() as false after allowedProviderHosts is saved', () => {
      // Arrange
      const { fixture, service, httpMock } = setupSettingsPage(SettingsProviderHostsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act — change and save
      const component = fixture.componentInstance as unknown as { _allowedHostsValue: { set: (v: string) => void } };
      component._allowedHostsValue.set('git.example.com');
      fixture.detectChanges();

      service.updateAllowedProviderHosts(['git.example.com']);
      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush({ ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com'] });
      fixture.detectChanges();

      // Assert — after save the textarea value matches saved, so not dirty
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });
  });
});
