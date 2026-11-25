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
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
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
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/limits' }));
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
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
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
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/dispatch' }));
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
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
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
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/dispatch' }));
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

  // ---------------------------------------------------------------------------
  // Webhooks section
  // ---------------------------------------------------------------------------

  function setupWebhooks(overrides: Record<string, unknown> = {}) {
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, ...overrides });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const saveBtn = Array.from(el.querySelectorAll('section'))
      .find(section => section.querySelector('h2')?.textContent?.trim() === 'Webhooks')!
      .querySelector('button.general-settings__save-btn') as HTMLButtonElement;
    const component = fixture.componentInstance as unknown as {
      _webhookUrlValue: { set: (v: string) => void };
      _backstopValue: { set: (v: number) => void };
    };
    return { fixture, httpMock, el, saveBtn, component };
  }

  const STORED_URL = 'https://hooks.example.com';
  const UNREACHABLE_PROBLEM = {
    type: 'tag:foundry,2026:problems/Settings.WebhookPublicBaseUrlUnreachable',
    detail: 'new.example.com didn\'t answer within 10 seconds.',
  };

  it('should initialize the webhook fields from loaded settings', () => {
    // Arrange & Act
    const { fixture } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL, backstopIntervalMinutes: 20 });

    // Assert
    expect(fixture.debugElement.query(By.css('#webhookPublicBaseUrl')).injector.get(NgModel).model).toBe(STORED_URL);
    expect(fixture.debugElement.query(By.css('#backstopIntervalMinutes')).injector.get(NgModel).model).toBe(20);
  });

  it('should show the webhook port, labelled, from the settings', () => {
    // Arrange & Act
    const { el } = setupWebhooks({ webhookPort: 6001 });

    // Assert
    const value = el.querySelector('.general-settings__readonly-value')!;
    expect(value.textContent?.trim()).toBe('6001');
    expect(value.getAttribute('aria-labelledby')).toBe('webhook-port-label');
    expect(el.querySelector('#webhook-port-label')?.textContent?.trim()).toBe('Webhook port');
  });

  it('should offer the tunnel and proxy examples for the port in a closed disclosure', () => {
    // Arrange & Act
    const { el } = setupWebhooks({ webhookPort: 6001 });

    // Assert
    const details = el.querySelector('details.general-settings__disclosure') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    expect(details.querySelector('summary')?.textContent?.trim()).toBe('Tunnel and proxy examples');
    expect(details.querySelector('fd-webhook-expose-guide')?.textContent).toContain('ngrok http 6001');
  });

  it('should send the trimmed URL with the backstop interval', () => {
    // Arrange
    const { fixture, httpMock, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });
    component._webhookUrlValue.set('  https://new.example.com ');
    component._backstopValue.set(30);
    fixture.detectChanges();

    // Act
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/webhooks');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ webhookPublicBaseUrl: 'https://new.example.com', backstopIntervalMinutes: 30 });
    req.flush(API_KEY_RESPONSE);
  });

  it('should require a URL and never send an empty one', () => {
    // Arrange
    const { fixture, httpMock, el, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });

    // Act
    component._webhookUrlValue.set('  ');
    fixture.detectChanges();
    saveBtn.click();

    // Assert
    expect(el.querySelector('#webhook-url-error')?.textContent).toBe('Enter the public https URL your tunnel or proxy serves.');
    expect(saveBtn.disabled).toBe(true);
    httpMock.expectNone('/api/settings/webhooks');
  });

  it('should show a field error and disable Save for a non-https URL', () => {
    // Arrange
    const { fixture, el, saveBtn, component } = setupWebhooks();

    // Act
    component._webhookUrlValue.set('http://hooks.example.com');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('#webhook-url-error')?.textContent).toBe('Use an absolute https URL, for example https://hooks.example.com.');
    expect(el.querySelector('#webhookPublicBaseUrl')?.getAttribute('aria-invalid')).toBe('true');
    expect(saveBtn.disabled).toBe(true);
  });

  it('should accept a localhost URL on the client, leaving host rules to the server', () => {
    // Arrange
    const { fixture, el, saveBtn, component } = setupWebhooks();

    // Act
    component._webhookUrlValue.set('https://localhost');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('#webhook-url-error')?.textContent).toBe('');
    expect(saveBtn.disabled).toBe(false);
  });

  it('should label Save "Verify and save" when the URL differs from the stored one, and "Save" otherwise', () => {
    // Arrange
    const { fixture, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });
    expect(saveBtn.textContent?.trim()).toBe('Save');

    // Act
    component._webhookUrlValue.set('https://new.example.com');
    fixture.detectChanges();

    // Assert
    expect(saveBtn.textContent?.trim()).toBe('Verify and save');
  });

  it('should show the probing status and keep Save focusable but inert while verifying a changed URL', () => {
    // Arrange
    const { fixture, httpMock, el, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });
    component._webhookUrlValue.set('https://new.example.com');
    fixture.detectChanges();

    // Act
    saveBtn.click();
    fixture.detectChanges();
    saveBtn.click();

    // Assert
    expect(saveBtn.textContent?.trim()).toBe('Verifying…');
    expect(saveBtn.disabled).toBe(false);
    expect(saveBtn.getAttribute('aria-disabled')).toBe('true');
    expect((el.querySelector('#webhookPublicBaseUrl') as HTMLInputElement).readOnly).toBe(true);
    expect(el.querySelector('#webhooks-probe-status')?.textContent?.trim())
      .toBe('Checking that new.example.com reaches this Foundry. This can take up to 10 seconds.');
    expect(el.querySelector('.general-settings__dispatch-form[aria-busy="true"]')).toBeTruthy();
    httpMock.expectOne('/api/settings/webhooks').flush({ ...API_KEY_RESPONSE, webhookPublicBaseUrl: 'https://new.example.com' });
  });

  it('should show "Saving…" and no probing status when only the backstop interval changed', () => {
    // Arrange
    const { fixture, httpMock, el, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });
    component._backstopValue.set(30);
    fixture.detectChanges();

    // Act
    saveBtn.click();
    fixture.detectChanges();

    // Assert
    expect(saveBtn.textContent?.trim()).toBe('Saving…');
    expect(el.querySelector('#webhooks-probe-status')?.textContent?.trim()).toBe('');
    httpMock.expectOne('/api/settings/webhooks').flush({ ...API_KEY_RESPONSE, webhookPublicBaseUrl: STORED_URL });
  });

  it('should confirm a verified URL change', () => {
    // Arrange
    const { fixture, httpMock, el, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });
    component._webhookUrlValue.set('https://new.example.com');
    fixture.detectChanges();

    // Act
    saveBtn.click();
    httpMock.expectOne('/api/settings/webhooks').flush({ ...API_KEY_RESPONSE, webhookPublicBaseUrl: 'https://new.example.com' });
    fixture.detectChanges();

    // Assert
    expect(el.textContent).toContain('Webhook URL verified and saved. Registered webhooks are being re-pointed to it.');
    expect(saveBtn.textContent?.trim()).toBe('Save');
  });

  it('should confirm a backstop-only save with the unchanged message', () => {
    // Arrange
    const { fixture, httpMock, el, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });
    component._backstopValue.set(30);
    fixture.detectChanges();

    // Act
    saveBtn.click();
    httpMock.expectOne('/api/settings/webhooks').flush({ ...API_KEY_RESPONSE, webhookPublicBaseUrl: STORED_URL, backstopIntervalMinutes: 30 });
    fixture.detectChanges();

    // Assert
    expect(el.textContent).toContain('Webhook settings saved successfully');
  });

  it('should show an unreachable error under the URL field, mark it invalid and focus it', async () => {
    // Arrange
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const { fixture, httpMock, el, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });
    component._webhookUrlValue.set('https://new.example.com');
    fixture.detectChanges();

    // Act
    saveBtn.click();
    httpMock.expectOne('/api/settings/webhooks').flush(UNREACHABLE_PROBLEM, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    await fixture.whenStable();

    // Assert
    const verifyError = el.querySelector('#webhook-url-verify-error')!;
    expect(verifyError.textContent).toBe(UNREACHABLE_PROBLEM.detail);
    expect(verifyError.getAttribute('role')).toBeNull();
    expect(el.querySelector('#webhook-url-error')?.textContent).toBe('');
    expect(el.querySelector('#webhooks-error')?.textContent?.trim()).toBe('');
    const input = el.querySelector('#webhookPublicBaseUrl') as HTMLInputElement;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe('webhook-url-hint webhook-url-error webhook-url-verify-error');
    expect(document.activeElement).toBe(input);
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/webhooks' }));
  });

  it('should key a server validation error to the URL field', () => {
    // Arrange
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const { fixture, httpMock, el, saveBtn, component } = setupWebhooks();
    component._webhookUrlValue.set('https://localhost');
    fixture.detectChanges();

    // Act
    saveBtn.click();
    httpMock.expectOne('/api/settings/webhooks').flush(
      { type: 'tag:foundry,2026:problems/Settings.InvalidWebhookPublicBaseUrl', detail: 'Bad URL from server' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('#webhook-url-verify-error')?.textContent).toBe('Bad URL from server');
    expect(el.querySelector('#webhooks-error')?.textContent?.trim()).toBe('');
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/webhooks' }));
  });

  it('should hide a server URL error once the URL is edited', () => {
    // Arrange
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const { fixture, httpMock, el, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });
    component._webhookUrlValue.set('https://new.example.com');
    fixture.detectChanges();
    saveBtn.click();
    httpMock.expectOne('/api/settings/webhooks').flush(UNREACHABLE_PROBLEM, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    // Act
    component._webhookUrlValue.set('https://newer.example.com');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('#webhook-url-verify-error')?.textContent).toBe('');
    expect(el.querySelector('#webhookPublicBaseUrl')?.getAttribute('aria-invalid')).toBeNull();
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/webhooks' }));
  });

  it('should show an error not keyed to a field in the general error', () => {
    // Arrange
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const { fixture, httpMock, el, saveBtn, component } = setupWebhooks({ webhookPublicBaseUrl: STORED_URL });
    component._webhookUrlValue.set('https://new.example.com');
    fixture.detectChanges();

    // Act
    saveBtn.click();
    httpMock.expectOne('/api/settings/webhooks').flush(
      { type: 'tag:foundry,2026:problems/Settings.NotFound', detail: 'Settings not found' },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('#webhooks-error')?.textContent?.trim()).toBe('Settings not found');
    expect(el.querySelector('#webhook-url-verify-error')?.textContent).toBe('');
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 404, url: '/api/settings/webhooks' }));
  });

  it('should report hasUnsavedChanges() as true when the backstop interval differs from saved value', () => {
    // Arrange
    const { fixture, component } = setupWebhooks({ backstopIntervalMinutes: 15 });

    // Act
    component._backstopValue.set(45);

    // Assert
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  // ---------------------------------------------------------------------------
  // Questions section (awaiting-input reminder)
  // ---------------------------------------------------------------------------

  function setupQuestions(overrides: Record<string, unknown> = {}) {
    const { fixture, httpMock } = setupSettingsPage(SettingsDispatchComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, ...overrides });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const saveBtn = Array.from(el.querySelectorAll('section'))
      .find(section => section.querySelector('h2')?.textContent?.trim() === 'Questions')!
      .querySelector('button.general-settings__save-btn') as HTMLButtonElement;
    const component = fixture.componentInstance as unknown as {
      _reminderDelayValue: { set: (v: number) => void };
    };
    return { fixture, httpMock, el, saveBtn, component };
  }

  it('should initialize the reminder delay from loaded settings', () => {
    // Arrange & Act
    const { fixture } = setupQuestions({ awaitingInputReminderDelayHours: 48 });

    // Assert
    expect(fixture.debugElement.query(By.css('#awaitingInputReminderDelayHours')).injector.get(NgModel).model).toBe(48);
  });

  it('should send the reminder delay when Save is clicked', () => {
    // Arrange
    const { fixture, httpMock, saveBtn, component } = setupQuestions({ awaitingInputReminderDelayHours: 72 });
    component._reminderDelayValue.set(24);
    fixture.detectChanges();

    // Act
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/awaiting-input');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ awaitingInputReminderDelayHours: 24 });
    req.flush({ ...API_KEY_RESPONSE, awaitingInputReminderDelayHours: 24 });
  });

  it('should show a field error and disable Save for a delay that is not positive', () => {
    // Arrange
    const { fixture, el, saveBtn, component } = setupQuestions();

    // Act
    component._reminderDelayValue.set(0);
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('#reminder-delay-error')?.textContent).toContain('between 1 and 8760');
    expect(el.querySelector('#awaitingInputReminderDelayHours')?.getAttribute('aria-invalid')).toBe('true');
    expect(saveBtn.disabled).toBe(true);
  });

  it('should show the server error when the save fails', () => {
    // Arrange
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const { fixture, httpMock, el, saveBtn } = setupQuestions();

    // Act
    saveBtn.click();
    httpMock.expectOne('/api/settings/awaiting-input').flush(
      { type: 'tag:foundry,2026:problems/Settings.InvalidAwaitingInputReminderDelay', detail: 'Bad delay from server' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('#awaiting-input-error')?.textContent).toContain('Bad delay from server');
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/awaiting-input' }));
  });

  it('should report hasUnsavedChanges() as true when the reminder delay differs from saved value', () => {
    // Arrange
    const { fixture, component } = setupQuestions({ awaitingInputReminderDelayHours: 72 });

    // Act
    component._reminderDelayValue.set(12);

    // Assert
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });
});
