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
import { SettingsDispatchComponent } from './settings-dispatch';

describe('SettingsDispatchComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // ---------------------------------------------------------------------------
  // Worker Limits section
  // ---------------------------------------------------------------------------

  it('should render the "Worker Limits" heading', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const headings = Array.from(el.querySelectorAll('h2'));
    const limitsHeading = headings.find(h => h.textContent?.trim() === 'Worker Limits');

    // Assert
    expect(limitsHeading).toBeTruthy();
  });

  it('should render the "Worker Limits" section with values from the service', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, maxConcurrent: 5, timeoutMinutes: 90 });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const maxInput = el.querySelector('#maxConcurrent') as HTMLInputElement;
    const timeoutInput = el.querySelector('#timeoutMinutes') as HTMLInputElement;

    // Assert
    expect(maxInput).toBeTruthy();
    expect(timeoutInput).toBeTruthy();
    const maxNgModel = fixture.debugElement.query(By.css('#maxConcurrent')).injector.get(NgModel);
    const timeoutNgModel = fixture.debugElement.query(By.css('#timeoutMinutes')).injector.get(NgModel);
    expect(maxNgModel.model).toBe(5);
    expect(timeoutNgModel.model).toBe(90);
  });

  it('should call updateWorkerLimits with current input values when Save is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const saveBtn = Array.from(el.querySelectorAll('button')).find(
      btn => btn.textContent?.trim() === 'Save' && btn.closest('.general-settings__limits-form')
    ) as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/limits');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ maxConcurrent: 3, timeoutMinutes: 60 });
    req.flush(API_KEY_RESPONSE);
  });

  it('should show "Worker limits saved successfully" after a successful save', () => {
    // Arrange
    const { fixture, httpMock, service } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updateWorkerLimits(3, 60);
    httpMock.expectOne('/api/settings/limits').flush(API_KEY_RESPONSE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const successEls = Array.from(el.querySelectorAll('[role="status"]'));
    const limitsSuccess = successEls.find(e => e.textContent?.includes('Worker limits saved successfully'));
    expect(limitsSuccess).toBeTruthy();
  });

  it('should show error message when save limits fails', () => {
    // Arrange
    const { fixture, httpMock, service } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updateWorkerLimits(3, 60);
    httpMock.expectOne('/api/settings/limits').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('#limits-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Failed to save worker limits');
  });

  it('should disable the Save button while limits are saving', () => {
    // Arrange
    const { fixture, httpMock, service } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updateWorkerLimits(3, 60);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const limitsForm = el.querySelector('.general-settings__limits-form') as HTMLElement;
    const saveBtn = limitsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
    expect(saveBtn.textContent?.trim()).toBe('Saving...');

    httpMock.expectOne('/api/settings/limits').flush(API_KEY_RESPONSE);
  });

  it('should disable the Save button when limits form is invalid (out of range)', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act — set maxConcurrent to 0 (below minimum of 1)
    const component = fixture.componentInstance as unknown as { _maxConcurrentValue: { set: (v: number) => void } };
    component._maxConcurrentValue.set(0);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const limitsForm = el.querySelector('.general-settings__limits-form') as HTMLElement;
    const saveBtn = limitsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
  });

  // ---------------------------------------------------------------------------
  // Dispatch Settings section
  // ---------------------------------------------------------------------------

  it('should render the "Dispatch Settings" section title', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const headings = Array.from(el.querySelectorAll('h2'));
    const dispatchHeading = headings.find(h => h.textContent?.trim() === 'Dispatch Settings');

    // Assert
    expect(dispatchHeading).toBeTruthy();
  });

  it('should initialize checkbox from settings autoResumeOnUsageReset value', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, autoResumeOnUsageReset: false });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelector('#autoResume') as HTMLInputElement;

    // Assert
    expect(checkbox).toBeTruthy();
    const ngModel = fixture.debugElement.query(By.css('#autoResume')).injector.get(NgModel);
    expect(ngModel.model).toBe(false);
  });

  it('should call updateDispatchSettings with autoResumeOnUsageReset when Save is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, autoResumeOnUsageReset: true });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/dispatch');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ autoResumeOnUsageReset: true, probeIntervalMinutes: 60, pollIntervalSeconds: 30 });
    req.flush(API_KEY_RESPONSE);
  });

  it('should render a "Credit check interval (minutes)" label and number input in the Dispatch Settings form', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const probeInput = el.querySelector('#probeIntervalMinutes') as HTMLInputElement;
    const probeLabel = el.querySelector('label[for="probeIntervalMinutes"]') as HTMLLabelElement;

    // Assert
    expect(probeInput).toBeTruthy();
    expect(probeInput.type).toBe('number');
    expect(probeLabel?.textContent?.trim()).toBe('Credit check interval (minutes)');
  });

  it('should describe the probe-interval field purpose in the hint text', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const hint = el.querySelector('#probe-interval-hint') as HTMLElement;

    // Assert — hint explains purpose and includes minimum
    expect(hint).toBeTruthy();
    expect(hint.textContent).toContain('Claude account');
    expect(hint.textContent).toContain('Minimum 5 minutes');
  });

  it('should conditionally bind aria-describedby on the autoResume checkbox based on dispatch error presence', () => {
    // Arrange
    const { fixture, httpMock, service } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act — no error initially
    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelector('#autoResume') as HTMLInputElement;

    // Assert — no aria-describedby when there is no save error
    expect(checkbox.getAttribute('aria-describedby')).toBeNull();

    // Act — trigger a dispatch save error
    service.updateDispatchSettings(true, 60, 30);
    httpMock.expectOne('/api/settings/dispatch').flush('Bad Request', { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    // Assert — aria-describedby is bound to dispatch-error when error is present
    expect(checkbox.getAttribute('aria-describedby')).toBe('dispatch-error');
  });

  it('should initialize probeIntervalMinutes input from loaded settings', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, probeIntervalMinutes: 30 });
    fixture.detectChanges();

    // Act
    const probeNgModel = fixture.debugElement.query(By.css('#probeIntervalMinutes')).injector.get(NgModel);

    // Assert
    expect(probeNgModel.model).toBe(30);
  });

  it('should show a validation error and set aria-invalid when probeIntervalMinutes is below the minimum (5)', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act — set value below minimum
    const component = fixture.componentInstance as unknown as { _probeIntervalValue: { set: (v: number) => void } };
    component._probeIntervalValue.set(3);
    fixture.detectChanges();

    // Assert — always-present error element has message text and input is aria-invalid
    const el = fixture.nativeElement as HTMLElement;
    const errorSpan = el.querySelector('#probe-interval-error[role="alert"]') as HTMLElement;
    expect(errorSpan).toBeTruthy();
    expect(errorSpan.textContent).toContain('at least 5 minutes');
    const probeInput = el.querySelector('#probeIntervalMinutes') as HTMLInputElement;
    expect(probeInput.getAttribute('aria-invalid')).toBe('true');
  });

  it('should render the probe-interval error element in the DOM at all times (always-present pattern)', () => {
    // Arrange — value is valid (at minimum)
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, probeIntervalMinutes: 5 });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const errorSpan = el.querySelector('#probe-interval-error[role="alert"]') as HTMLElement;

    // Assert — element is always in DOM; content is empty when valid; aria-invalid is absent
    expect(errorSpan).toBeTruthy();
    expect(errorSpan.textContent?.trim()).toBe('');
    const probeInput = el.querySelector('#probeIntervalMinutes') as HTMLInputElement;
    expect(probeInput.getAttribute('aria-invalid')).toBeNull();
  });

  it('should disable the Save button when probeIntervalMinutes is below the minimum', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act — set value below minimum
    const component = fixture.componentInstance as unknown as { _probeIntervalValue: { set: (v: number) => void } };
    component._probeIntervalValue.set(2);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
  });

  it('should include probeIntervalMinutes in the dispatch settings save payload', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, probeIntervalMinutes: 45, pollIntervalSeconds: 90, autoResumeOnUsageReset: false });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/dispatch');
    expect(req.request.body).toEqual({ autoResumeOnUsageReset: false, probeIntervalMinutes: 45, pollIntervalSeconds: 90 });
    req.flush(API_KEY_RESPONSE);
  });

  it('should disable Save button while dispatch settings are saving', () => {
    // Arrange
    const { fixture, httpMock, service } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updateDispatchSettings(true, 60, 30);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
    expect(saveBtn.textContent?.trim()).toBe('Saving...');

    httpMock.expectOne('/api/settings/dispatch').flush(API_KEY_RESPONSE);
  });

  it('should show "Dispatch settings saved successfully" after a successful save', () => {
    // Arrange
    const { fixture, httpMock, service } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updateDispatchSettings(true, 60, 30);
    httpMock.expectOne('/api/settings/dispatch').flush(API_KEY_RESPONSE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const successEls = Array.from(el.querySelectorAll('[role="status"]'));
    const dispatchSuccess = successEls.find(e => e.textContent?.includes('Dispatch settings saved successfully'));
    expect(dispatchSuccess).toBeTruthy();
  });

  it('should show error message when dispatch save fails', () => {
    // Arrange
    const { fixture, httpMock, service } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updateDispatchSettings(true, 60, 30);
    httpMock.expectOne('/api/settings/dispatch').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('#dispatch-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Failed to save dispatch settings');
  });

  // ---------------------------------------------------------------------------
  // UnsavedChanges guard
  // ---------------------------------------------------------------------------

  it('should report hasUnsavedChanges() as false when limits match saved values', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, maxConcurrent: 3, timeoutMinutes: 60 });
    fixture.detectChanges();

    // Assert
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('should report hasUnsavedChanges() as true when maxConcurrent differs from saved value', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, maxConcurrent: 3, timeoutMinutes: 60 });
    fixture.detectChanges();

    // Act — change the value
    const component = fixture.componentInstance as unknown as { _maxConcurrentValue: { set: (v: number) => void } };
    component._maxConcurrentValue.set(5);
    fixture.detectChanges();

    // Assert
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('should report hasUnsavedChanges() as true when autoResumeOnUsageReset differs from saved value', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, autoResumeOnUsageReset: true });
    fixture.detectChanges();

    // Act — change autoResume
    const component = fixture.componentInstance as unknown as { _autoResumeValue: { set: (v: boolean) => void } };
    component._autoResumeValue.set(false);
    fixture.detectChanges();

    // Assert
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('should report hasUnsavedChanges() as false when saved state is null (no settings loaded yet)', () => {
    // Arrange — settings requests are in-flight but not yet resolved, so workerLimits() and settings() are null
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();

    // Assert — null saved state is treated as NOT dirty
    const result = fixture.componentInstance.hasUnsavedChanges();

    // Flush pending requests before afterEach.verify() runs
    flushSettings(httpMock);
    fixture.detectChanges();

    expect(result).toBe(false);
  });
});
