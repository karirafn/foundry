import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { SettingsService } from '../../../core/services/settings.service';
import {
  setupSettingsPage,
  flushSettings,
  API_KEY_RESPONSE,
  OAUTH_RESPONSE,
  CREDENTIALS_API_KEY,
  CREDENTIALS_API_KEY_NOT_CONFIGURED,
  CREDENTIALS_API_KEY_UNREADABLE,
  CREDENTIALS_OAUTH,
  CREDENTIALS_OAUTH_NOT_CONFIGURED,
  CREDENTIALS_OAUTH_RELOGIN_NEEDED,
} from '../testing/settings-page-harness';
import { SettingsClaudeCodeComponent } from './settings-claude-code';

describe('SettingsClaudeCodeComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should render the "Worker Authentication" section title', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const titles = Array.from(el.querySelectorAll('.general-settings__section-title'));
    const authTitle = titles.find(t => t.textContent?.includes('Worker Authentication'));

    // Assert
    expect(authTitle).toBeTruthy();
  });

  it('should set tabindex="-1" on the Worker Authentication heading for focus management', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication'));

    // Assert
    expect(authHeading?.getAttribute('tabindex')).toBe('-1');
  });

  it('should render API Key and OAuth radio options', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll('input[type="radio"]');

    // Assert
    expect(radios.length).toBe(2);
  });

  it('should render the API key input when mode is api_key', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('.general-settings__api-key-input');

    // Assert
    expect(input).toBeTruthy();
  });

  it('should not render the API key input when mode is oauth', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('.general-settings__api-key-input');

    // Assert
    expect(input).toBeFalsy();
  });

  it('should render fd-oauth-panel when mode is oauth', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const panel = el.querySelector('fd-oauth-panel');

    // Assert
    expect(panel).toBeTruthy();
  });

  it('should wrap the radio group in a fieldset with screen-reader legend', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const fieldset = el.querySelector('fieldset.general-settings__mode-fieldset');
    const legend = fieldset?.querySelector('legend');

    // Assert
    expect(fieldset).toBeTruthy();
    expect(legend?.classList.contains('sr-only')).toBe(true);
  });

  it('should set aria-invalid on the API key input when saveError is present', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updateAuthMode('api_key', 'bad-key');
    httpMock.expectOne('/api/credentials/auth').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('.general-settings__api-key-input') as HTMLInputElement;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const errorEl = el.querySelector('#api-key-error');
    expect(errorEl).toBeTruthy();
  });

  it('should call updateAuthMode when saveApiKey is invoked', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const component = fixture.componentInstance as unknown as { _apiKeyValue: string };
    component._apiKeyValue = 'test-key';
    fixture.componentInstance.saveApiKey();

    // Assert
    const req = httpMock.expectOne('/api/credentials/auth');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ mode: 'api_key', apiKey: 'test-key' });
    req.flush(CREDENTIALS_API_KEY);
  });

  it('should show "Switch account" button when OAuth mode is active and status is Present', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn');

    // Assert
    expect(switchBtn).toBeTruthy();
  });

  it('should not show "Switch account" button when OAuth status is NotConfigured', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH_NOT_CONFIGURED);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn');

    // Assert
    expect(switchBtn).toBeFalsy();
  });

  it('should show confirm group when "Switch account" is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Assert — uses role="group", not role="dialog"
    const group = el.querySelector('[role="group"][aria-label="Confirm account switch"]');
    expect(group?.textContent).toContain('Signing in will pause dispatch');
  });

  it('should use role="group" (not role="dialog") for the inline confirm', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Assert — no dialog role
    expect(el.querySelector('[role="dialog"]')).toBeFalsy();
    expect(el.querySelector('[role="group"]')).toBeTruthy();
  });

  it('should return focus to the auth heading when Cancel is clicked', async () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const cancelBtn = el.querySelector('.general-settings__switch-cancel-btn') as HTMLButtonElement;
    cancelBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — focus returns to auth heading
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication')) as HTMLElement;
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should call pauseDispatch and startLogin when confirm button is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();

    // Assert — pauseDispatch fires and startLogin fires
    const pauseReq = httpMock.expectOne('/api/settings/dispatch/pause');
    expect(pauseReq.request.method).toBe('POST');
    pauseReq.flush(OAUTH_RESPONSE);

    const loginReq = httpMock.expectOne('/api/credentials/login/start');
    expect(loginReq.request.method).toBe('POST');
    loginReq.flush({ sessionId: 'test-session' });
  });

  it('should move focus to the auth heading after confirming account switch', async () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Assert — focus moves to the auth heading
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication')) as HTMLElement;
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should announce drain text via text binding in the persistent role="status" element (not via @if content insertion)', () => {
    // Arrange — enter draining state
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;

    // Assert — the role="status" drain-gate element is present even before draining (empty)
    const drainGate = el.querySelector('.general-settings__drain-gate[role="status"]') as HTMLElement;
    expect(drainGate).toBeTruthy();
    expect(drainGate.textContent?.trim()).toBe('');

    // Act — trigger drain by confirming account switch
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Assert — same persistent element now has content
    const drainGateAfter = el.querySelector('.general-settings__drain-gate[role="status"]') as HTMLElement;
    expect(drainGateAfter).toBe(drainGate); // same DOM node
    expect(drainGateAfter.textContent).toContain('Dispatch is paused');
  });

  it('should show drain gate message after confirming account switch', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Assert
    const drainGate = el.querySelector('.general-settings__drain-gate');
    expect(drainGate?.textContent).toContain('Dispatch is paused');
  });

  it('should keep the drain-gate role="status" wrapper in the DOM when OAuth mode is active', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Assert — the live-region wrapper must always be present (empty when not draining)
    const el = fixture.nativeElement as HTMLElement;
    const drainStatus = el.querySelector('.general-settings__drain-gate[role="status"]');
    expect(drainStatus).toBeTruthy();
    expect(drainStatus?.textContent?.trim()).toBe('');
  });

  it('should render the switch-error role="alert" as a sibling of drain-gate, not nested inside it', () => {
    // Arrange — switch account and fail the pause request to trigger a pauseResumeError
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Assert — role="alert" must NOT be a descendant of role="status"
    const drainGate = el.querySelector('.general-settings__drain-gate[role="status"]');
    const alertInsideDrainGate = drainGate?.querySelector('[role="alert"]');
    expect(alertInsideDrainGate).toBeFalsy();

    // Assert — a sibling role="alert" for the error should exist outside the drain-gate
    const oauthSection = el.querySelector('.general-settings__oauth-section');
    const siblingAlert = oauthSection?.querySelector('.general-settings__switch-error[role="alert"]');
    expect(siblingAlert).toBeTruthy();
  });

  it('should show confirm dialog when "Log in again" is clicked (ReLoginNeeded status)', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH_RELOGIN_NEEDED);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const loginAgainBtn = el.querySelector('.oauth-panel__login-btn') as HTMLButtonElement;
    loginAgainBtn.click();
    fixture.detectChanges();

    // Assert — confirm group appears (same drain flow as Switch account)
    const group = el.querySelector('[role="group"][aria-label="Confirm account switch"]');
    expect(group).toBeTruthy();
    expect(group?.textContent).toContain('Signing in will pause dispatch');
  });

  it('should reset drain-gate state when login phase becomes null (succeeds)', () => {
    // Arrange — enter draining state via switch account
    const { fixture, service, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Verify draining
    let drainGate = el.querySelector('.general-settings__drain-gate');
    expect(drainGate?.textContent).toContain('Dispatch is paused');

    // Act — simulate the service clearing loginPhase to null after settings reload
    (service as unknown as { _loginPhaseSignal: { set: (v: string | null) => void } })._loginPhaseSignal.set(null);
    fixture.detectChanges();

    // Assert — drain gate resets
    drainGate = el.querySelector('.general-settings__drain-gate');
    expect(drainGate?.textContent?.trim()).toBe('');
  });

  it('should show Resume button when login fails while switch-account drain is active', () => {
    // Arrange — enter draining state
    const { fixture, service, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Act — simulate Failed phase while still draining
    (service as unknown as { _loginPhaseSignal: { set: (v: string) => void } })._loginPhaseSignal.set('Failed');
    fixture.detectChanges();

    // Assert — Resume button is visible and NOT nested inside the drain-gate live region
    const resumeBtn = el.querySelector('.general-settings__resume-btn');
    expect(resumeBtn).toBeTruthy();

    const drainGate = el.querySelector('.general-settings__drain-gate[role="status"]');
    const resumeInsideDrainGate = drainGate?.querySelector('.general-settings__resume-btn');
    expect(resumeInsideDrainGate).toBeFalsy();
  });

  it('should reset drain-gate state when startLogin POST fails while draining', () => {
    // Arrange — enter draining state
    const { fixture, service, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);

    // Fail the login start POST
    httpMock.expectOne('/api/credentials/login/start').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — drain gate is cleared (not stranded)
    const drainGate = el.querySelector('.general-settings__drain-gate');
    expect(drainGate?.textContent?.trim()).toBe('');
  });

  it('should move focus to auth heading when login phase becomes Succeeded', async () => {
    // Arrange
    const { fixture, service, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act — simulate Succeeded phase
    (service as unknown as { _loginPhaseSignal: { set: (v: string) => void } })._loginPhaseSignal.set('Succeeded');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — focus moves to auth heading
    const el = fixture.nativeElement as HTMLElement;
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication')) as HTMLElement;
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should show startLoginError in a role="alert" when loginPhase is null and error is set', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH_NOT_CONFIGURED);
    fixture.detectChanges();

    // Act — startLogin fails (POST 500)
    const service = TestBed.inject(SettingsService);
    service.startLogin();
    httpMock.expectOne('/api/credentials/login/start').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — startLoginError displayed
    const el = fixture.nativeElement as HTMLElement;
    const alerts = Array.from(el.querySelectorAll('[role="alert"]')) as HTMLElement[];
    const startLoginAlert = alerts.find(a => a.textContent?.includes('Failed to start login'));
    expect(startLoginAlert).toBeTruthy();
  });

  it('should focus the entry login button on cancel when login flow is not in drain state', async () => {
    // Arrange — Not-Configured state, no drain gate, login button is visible
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH_NOT_CONFIGURED);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;

    // Act — cancel login
    fixture.componentInstance.onCancelLogin();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — focus goes to the Log in entry button
    const loginBtn = el.querySelector('.oauth-panel__login-btn') as HTMLButtonElement;
    expect(loginBtn).toBeTruthy();
    expect(document.activeElement).toBe(loginBtn);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should focus auth heading on cancel when no entry login button is reachable', async () => {
    // Arrange — api_key mode, so no oauth-panel__login-btn or switch-btn visible
    const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;

    // Act — cancel login; no oauth panel rendered so no entry button exists
    fixture.componentInstance.onCancelLogin();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — falls back to auth heading
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication')) as HTMLElement;
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  describe('API-key status indicator', () => {
    it('should render the configured-indicator when apiKeyStatus is Present', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const indicator = el.querySelector('#api-key-configured .general-settings__configured-indicator');

      // Assert
      expect(indicator).toBeTruthy();
      expect(indicator?.textContent).toContain('API key is configured');
    });

    it('should render the muted hint when apiKeyStatus is NotConfigured', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const hint = el.querySelector('#api-key-configured .general-settings__field-hint--credential-status');

      // Assert
      expect(hint).toBeTruthy();
      expect(hint?.textContent).toContain('No API key is set');
    });

    it('should render the warning box with role="alert" when apiKeyStatus is Unreadable', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_UNREADABLE);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const warningRegion = el.querySelector('#api-key-configured[role="alert"]');
      const warning = el.querySelector('.general-settings__api-key-warning');

      // Assert — the persistent wrapper carries role="alert" and contains the warning content
      expect(warningRegion).toBeTruthy();
      expect(warning).toBeTruthy();
      expect(warning?.textContent).toContain('can no longer be read');
      expect(warning?.textContent).toContain('Re-enter');
    });

    it('should keep id="api-key-configured" always in the DOM so the input aria-describedby never dangles', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('#api-key-configured');
      const input = el.querySelector('.general-settings__api-key-input');

      // Assert
      expect(statusEl).toBeTruthy();
      expect(input?.getAttribute('aria-describedby')).toContain('api-key-configured');
    });

    it('should keep id="api-key-configured" in the DOM even when apiKeyStatus is Present', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('#api-key-configured');

      // Assert — persistent wrapper always in the DOM
      expect(statusEl).toBeTruthy();
    });

    it('should keep id="api-key-configured" in the DOM when apiKeyStatus is Unreadable', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_UNREADABLE);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('#api-key-configured');

      // Assert
      expect(statusEl).toBeTruthy();
    });

    it('should set role="alert" on the persistent wrapper only when apiKeyStatus is Unreadable', () => {
      // Arrange — Present state: no role="alert" on the wrapper
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const wrapper = el.querySelector('#api-key-configured');

      // Assert — no alert role when not Unreadable
      expect(wrapper?.getAttribute('role')).not.toBe('alert');
    });

    it('should not render the configured-indicator when apiKeyStatus is NotConfigured', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const indicator = el.querySelector('.general-settings__configured-indicator');

      // Assert
      expect(indicator).toBeFalsy();
    });

    it('should render the NotConfigured hint with the credential-status class', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const hint = el.querySelector('.general-settings__field-hint--credential-status');

      // Assert
      expect(hint).toBeTruthy();
      expect(hint?.textContent).toContain('No API key is set');
    });
  });

  describe('UnsavedChanges', () => {
    it('should report hasUnsavedChanges as false when no API key is typed', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Assert — no typed key, so not dirty
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });

    it('should report hasUnsavedChanges as true when an API key is typed and mode is api_key', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act — type a key
      const component = fixture.componentInstance as unknown as { _apiKeyValue: string };
      component._apiKeyValue = 'sk-new-key';
      fixture.detectChanges();

      // Assert — api_key mode + typed value = dirty
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should report hasUnsavedChanges as false when mode is oauth even if _apiKeyValue is set', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsClaudeCodeComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
      fixture.detectChanges();

      // Act — set a key value while in oauth mode
      const component = fixture.componentInstance as unknown as { _apiKeyValue: string };
      component._apiKeyValue = 'sk-irrelevant';
      fixture.detectChanges();

      // Assert — oauth mode, so not dirty
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });
  });
});
