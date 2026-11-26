import { TestBed, ComponentFixture } from '@angular/core/testing';
import { LoginFlowComponent } from './login-flow';
import { LoginError, LoginPhase } from '../../../../core/models/settings.model';

function setup(inputs: {
  phase: LoginPhase;
  url?: string | null;
  error?: LoginError | null;
  accountEmail?: string | null;
  codeError?: string | null;
}): ComponentFixture<LoginFlowComponent> {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [LoginFlowComponent],
  });
  const fixture = TestBed.createComponent(LoginFlowComponent);
  fixture.componentRef.setInput('phase', inputs.phase);
  fixture.componentRef.setInput('url', inputs.url ?? null);
  fixture.componentRef.setInput('error', inputs.error ?? null);
  fixture.componentRef.setInput('accountEmail', inputs.accountEmail ?? null);
  fixture.componentRef.setInput('codeError', inputs.codeError ?? null);
  fixture.detectChanges();
  return fixture;
}

const SERVER_DETAIL = "That doesn't look like the full code. Copy all of it, including the # in the middle.";

function codeInput(fixture: ComponentFixture<LoginFlowComponent>): HTMLInputElement {
  return (fixture.nativeElement as HTMLElement).querySelector('input[aria-label="Authorization code"]') as HTMLInputElement;
}

function typeCode(fixture: ComponentFixture<LoginFlowComponent>, value: string): void {
  const input = codeInput(fixture);
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function clickSignIn(fixture: ComponentFixture<LoginFlowComponent>): void {
  const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button');
  const signInBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Sign in') as HTMLButtonElement;
  signInBtn.click();
}

function liveRegion(fixture: ComponentFixture<LoginFlowComponent>): HTMLElement {
  return (fixture.nativeElement as HTMLElement).querySelector('.login-flow__sr-live') as HTMLElement;
}

describe('LoginFlowComponent', () => {
  describe('Starting phase', () => {
    it('should show spinner in Starting phase', () => {
      // Arrange
      const fixture = setup({ phase: 'Starting' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const spinner = el.querySelector('fd-spinner');

      // Assert
      expect(spinner).toBeTruthy();
    });

    it('should render fd-spinner at the 14px default diameter in Starting phase', () => {
      // Arrange
      const fixture = setup({ phase: 'Starting' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const innerSpan = el.querySelector('fd-spinner span.spinner') as HTMLElement;

      // Assert
      expect(innerSpan.style.width).toBe('14px');
      expect(innerSpan.style.height).toBe('14px');
    });

    it('should show "Starting sign-in…" text in Starting phase', () => {
      // Arrange
      const fixture = setup({ phase: 'Starting' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Starting sign-in…');
    });

    it('should have role="status" with aria-live="polite" in Starting phase', () => {
      // Arrange
      const fixture = setup({ phase: 'Starting' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert
      expect(statusEl).toBeTruthy();
    });

    it('should NOT show code input in Starting phase', () => {
      // Arrange
      const fixture = setup({ phase: 'Starting' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[type="text"]');

      // Assert
      expect(input).toBeFalsy();
    });
  });

  describe('WaitingForAuthorization phase', () => {
    const url = 'https://claude.ai/oauth/authorize?code=abc123';

    it('should show the OAuth URL as a real anchor link', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const anchor = el.querySelector('a[href]') as HTMLAnchorElement | null;

      // Assert
      expect(anchor).toBeTruthy();
      expect(anchor?.href).toContain('claude.ai');
    });

    it('should set target="_blank" and rel="noopener noreferrer" on the URL link', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const anchor = el.querySelector('a[href]') as HTMLAnchorElement | null;

      // Assert
      expect(anchor?.getAttribute('target')).toBe('_blank');
      expect(anchor?.getAttribute('rel')).toBe('noopener noreferrer');
    });

    it('should show code input with aria-label "Authorization code"', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[aria-label="Authorization code"]');

      // Assert
      expect(input).toBeTruthy();
    });

    it('should show "Sign in" submit button', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const buttons = el.querySelectorAll('button');
      const signInBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Sign in');

      // Assert
      expect(signInBtn).toBeTruthy();
    });

    it('should show "Cancel" button in WaitingForAuthorization', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const buttons = el.querySelectorAll('button');
      const cancelBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Cancel');

      // Assert
      expect(cancelBtn).toBeTruthy();
    });

    it('should disable Sign in button when code input is empty', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const buttons = el.querySelectorAll('button');
      const signInBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Sign in') as HTMLButtonElement | undefined;

      // Assert
      expect(signInBtn?.disabled).toBe(true);
    });

    it('should enable Sign in button when code input has non-whitespace value', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[aria-label="Authorization code"]') as HTMLInputElement;
      input.value = 'my-code';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      const buttons = el.querySelectorAll('button');
      const signInBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Sign in') as HTMLButtonElement | undefined;

      // Assert
      expect(signInBtn?.disabled).toBe(false);
    });

    it('should disable Sign in button when code input is only whitespace', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[aria-label="Authorization code"]') as HTMLInputElement;
      input.value = '   ';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      const buttons = el.querySelectorAll('button');
      const signInBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Sign in') as HTMLButtonElement | undefined;

      // Assert
      expect(signInBtn?.disabled).toBe(true);
    });

    it('should emit submitCode with trimmed code when Sign in is clicked', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });
      let emittedCode: string | undefined;
      fixture.componentInstance.submitCode.subscribe((code: string) => (emittedCode = code));

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[aria-label="Authorization code"]') as HTMLInputElement;
      input.value = '  abc123  ';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      const buttons = el.querySelectorAll('button');
      const signInBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Sign in') as HTMLButtonElement;
      signInBtn.click();

      // Assert
      expect(emittedCode).toBe('abc123');
    });

    it('should emit cancel when Cancel button is clicked', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });
      let emitted = false;
      fixture.componentInstance.cancel.subscribe(() => (emitted = true));

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const buttons = el.querySelectorAll('button');
      const cancelBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Cancel') as HTMLButtonElement;
      cancelBtn.click();

      // Assert
      expect(emitted).toBe(true);
    });

    it('should show hint text "After you authorize, Claude shows a code — paste it here."', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('After you authorize, Claude shows a code');
    });

    it('should submit on Enter key press in code input', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });
      let emittedCode: string | undefined;
      fixture.componentInstance.submitCode.subscribe((code: string) => (emittedCode = code));

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[aria-label="Authorization code"]') as HTMLInputElement;
      input.value = 'enter-code';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

      // Assert
      expect(emittedCode).toBe('enter-code');
    });

    it('should NOT emit submitCode on Enter when code is empty', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });
      let emittedCode: string | undefined;
      fixture.componentInstance.submitCode.subscribe((code: string) => (emittedCode = code));

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[aria-label="Authorization code"]') as HTMLInputElement;
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

      // Assert
      expect(emittedCode).toBeUndefined();
    });

    it('should show the "Open this link to authorize" step label', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Open this link to authorize');
    });

    it('should show the "Paste the code from your browser" step label', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Paste the code from your browser');
    });
  });

  describe('refused code', () => {
    const url = 'https://claude.ai/oauth/authorize?code=abc123';

    it('should mark the code input invalid when a code error is shown', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url, codeError: SERVER_DETAIL });

      // Act
      const input = codeInput(fixture);

      // Assert
      expect(input.getAttribute('aria-invalid')).toBe('true');
    });

    it('should describe the code input by the error before the hint', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url, codeError: SERVER_DETAIL });

      // Act
      const input = codeInput(fixture);

      // Assert
      expect(input.getAttribute('aria-describedby')).toBe('login-flow-code-error login-flow-code-hint');
    });

    it('should show the code error verbatim below the input', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url, codeError: SERVER_DETAIL });

      // Act
      const message = (fixture.nativeElement as HTMLElement).querySelector('#login-flow-code-error.login-flow__code-error');

      // Assert
      expect(message?.textContent?.trim()).toBe(SERVER_DETAIL);
    });

    it('should show no error, no aria-invalid and only the hint description without a code error', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = codeInput(fixture);

      // Assert
      expect(el.querySelector('.login-flow__code-error')).toBeNull();
      expect(input.hasAttribute('aria-invalid')).toBe(false);
      expect(input.getAttribute('aria-describedby')).toBe('login-flow-code-hint');
    });

    it('should announce the code error in the live region', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url, codeError: SERVER_DETAIL });

      // Act
      const text = liveRegion(fixture).textContent?.trim();

      // Assert
      expect(text).toBe(SERVER_DETAIL);
    });

    it('should hide the error and aria-invalid on the first edit without changing the live region', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url, codeError: SERVER_DETAIL });
      const liveBefore = liveRegion(fixture).textContent;

      // Act
      typeCode(fixture, 'INVALID-CODE#');

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('.login-flow__code-error')).toBeNull();
      expect(codeInput(fixture).hasAttribute('aria-invalid')).toBe(false);
      expect(liveRegion(fixture).textContent).toBe(liveBefore);
    });

    it('should refill, focus and select the submitted code when the refused session returns to waiting', async () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });
      typeCode(fixture, 'INVALID-CODE');
      clickSignIn(fixture);
      fixture.componentRef.setInput('phase', 'SigningIn');
      fixture.detectChanges();

      // Act
      fixture.componentRef.setInput('phase', 'WaitingForAuthorization');
      fixture.componentRef.setInput('codeError', SERVER_DETAIL);
      fixture.detectChanges();
      await fixture.whenStable();

      // Assert
      const input = codeInput(fixture);
      expect(input.value).toBe('INVALID-CODE');
      expect(document.activeElement).toBe(input);
      expect(input.selectionStart).toBe(0);
      expect(input.selectionEnd).toBe('INVALID-CODE'.length);
    });

    it('should keep the code on submit and empty it once the session fails and restarts', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });
      typeCode(fixture, 'some-code');

      // Act
      clickSignIn(fixture);
      fixture.detectChanges();
      const valueAfterSubmit = codeInput(fixture).value;
      for (const phase of ['Failed', 'Starting', 'WaitingForAuthorization'] as const) {
        fixture.componentRef.setInput('phase', phase);
        fixture.detectChanges();
      }

      // Assert
      expect(valueAfterSubmit).toBe('some-code');
      expect(codeInput(fixture).value).toBe('');
    });

    it('should show the message again when an edited code is refused for the same reason', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url, codeError: SERVER_DETAIL });
      typeCode(fixture, 'INVALID-CODE2');
      clickSignIn(fixture);
      fixture.componentRef.setInput('phase', 'SigningIn');
      fixture.componentRef.setInput('codeError', null);
      fixture.detectChanges();

      // Act
      fixture.componentRef.setInput('phase', 'WaitingForAuthorization');
      fixture.componentRef.setInput('codeError', SERVER_DETAIL);
      fixture.detectChanges();

      // Assert
      const message = (fixture.nativeElement as HTMLElement).querySelector('.login-flow__code-error');
      expect(message?.textContent?.trim()).toBe(SERVER_DETAIL);
    });

    it('should emit submitCode again when the same code is resubmitted', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url });
      const emitted: string[] = [];
      fixture.componentInstance.submitCode.subscribe((code: string) => emitted.push(code));
      typeCode(fixture, 'INVALID-CODE');
      clickSignIn(fixture);
      fixture.componentRef.setInput('codeError', SERVER_DETAIL);
      fixture.detectChanges();

      // Act
      clickSignIn(fixture);

      // Assert
      expect(emitted).toEqual(['INVALID-CODE', 'INVALID-CODE']);
    });
  });

  describe('SigningIn phase', () => {
    it('should show spinner in SigningIn phase', () => {
      // Arrange
      const fixture = setup({ phase: 'SigningIn' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const spinner = el.querySelector('fd-spinner');

      // Assert
      expect(spinner).toBeTruthy();
    });

    it('should render fd-spinner at the 14px default diameter in SigningIn phase', () => {
      // Arrange
      const fixture = setup({ phase: 'SigningIn' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const innerSpan = el.querySelector('fd-spinner span.spinner') as HTMLElement;

      // Assert
      expect(innerSpan.style.width).toBe('14px');
      expect(innerSpan.style.height).toBe('14px');
    });

    it('should show "Signing you in…" text in SigningIn phase', () => {
      // Arrange
      const fixture = setup({ phase: 'SigningIn' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Signing you in…');
    });

    it('should NOT show code input in SigningIn phase (removed, not just disabled)', () => {
      // Arrange
      const fixture = setup({ phase: 'SigningIn' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[type="text"]');

      // Assert
      expect(input).toBeFalsy();
    });

    it('should NOT show Sign in button in SigningIn phase', () => {
      // Arrange
      const fixture = setup({ phase: 'SigningIn' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const buttons = el.querySelectorAll('button');
      const signInBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Sign in');

      // Assert
      expect(signInBtn).toBeFalsy();
    });
  });

  describe('Failed phase', () => {
    it('should show "Try again" button in Failed phase', () => {
      // Arrange
      const fixture = setup({ phase: 'Failed', error: 'Unknown' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const buttons = el.querySelectorAll('button');
      const retryBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Try again');

      // Assert
      expect(retryBtn).toBeTruthy();
    });

    it('should emit retry when Try again is clicked', () => {
      // Arrange
      const fixture = setup({ phase: 'Failed', error: 'Unknown' });
      let emitted = false;
      fixture.componentInstance.retry.subscribe(() => (emitted = true));

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const buttons = el.querySelectorAll('button');
      const retryBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Try again') as HTMLButtonElement;
      retryBtn.click();

      // Assert
      expect(emitted).toBe(true);
    });

    it('should announce failure heading in the persistent role="status" live region (not via @if-mounted alert)', () => {
      // Arrange
      const fixture = setup({ phase: 'Failed', error: 'Unknown' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert — live region text contains the failure heading so SR users hear it
      expect(statusEl?.textContent).toContain('Sign-in failed');
      // The @if-mounted failed div must NOT carry role="alert"
      const failedDiv = el.querySelector('.login-flow__phase--failed');
      expect(failedDiv?.getAttribute('role')).not.toBe('alert');
    });

    it('should show InvalidCode error copy', () => {
      // Arrange
      const fixture = setup({ phase: 'Failed', error: 'InvalidCode' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain("That code didn't work");
      expect(el.textContent).toContain('The code was wrong or expired');
    });

    it('should show UrlTimeout error copy', () => {
      // Arrange
      const fixture = setup({ phase: 'Failed', error: 'UrlTimeout' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain("Sign-in didn't start");
      expect(el.textContent).toContain("couldn't reach Claude");
    });

    it('should show CodeTimeout error copy', () => {
      // Arrange
      const fixture = setup({ phase: 'Failed', error: 'CodeTimeout' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Sign-in timed out');
      expect(el.textContent).toContain("didn't finish in time");
    });

    it('should show Unknown fallback error copy', () => {
      // Arrange
      const fixture = setup({ phase: 'Failed', error: 'Unknown' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Sign-in failed');
      expect(el.textContent).toContain('Something went wrong');
    });

    it('should show fallback error copy when error is null', () => {
      // Arrange
      const fixture = setup({ phase: 'Failed', error: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Sign-in failed');
    });
  });

  describe('Live region announcements', () => {
    it('should have a polite live region that persists across phases', () => {
      // Arrange
      const fixture = setup({ phase: 'Starting' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const liveRegion = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert
      expect(liveRegion).toBeTruthy();
    });

    it('should announce "Starting sign-in…" in Starting phase', () => {
      // Arrange
      const fixture = setup({ phase: 'Starting' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert
      expect(statusEl?.textContent).toContain('Starting sign-in');
    });

    it('should announce sign-in link ready text in WaitingForAuthorization phase', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url: 'https://claude.ai/oauth' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert
      expect(statusEl?.textContent).toContain('Sign-in link ready');
    });

    it('should announce "Signing you in…" in SigningIn phase', () => {
      // Arrange
      const fixture = setup({ phase: 'SigningIn' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert
      expect(statusEl?.textContent).toContain('Signing you in');
    });

    it('should announce "Signed in as {email}." in Succeeded phase when accountEmail is provided', () => {
      // Arrange
      const fixture = setup({ phase: 'Succeeded', accountEmail: 'user@example.com' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert
      expect(statusEl?.textContent).toContain('Signed in as user@example.com.');
    });

    it('should announce "Signed in." in Succeeded phase when accountEmail is null', () => {
      // Arrange
      const fixture = setup({ phase: 'Succeeded', accountEmail: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert
      expect(statusEl?.textContent?.trim()).toBe('Signed in.');
    });

    it('should announce the failure heading in the live region for Failed phase', () => {
      // Arrange
      const fixture = setup({ phase: 'Failed', error: 'InvalidCode' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert — liveText returns the error heading so SR users are informed
      expect(statusEl?.textContent).toContain("That code didn't work");
    });
  });

  describe('Code input security', () => {
    it('should set autocomplete="off" on code input', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url: 'https://claude.ai/oauth' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[aria-label="Authorization code"]');

      // Assert
      expect(input?.getAttribute('autocomplete')).toBe('off');
    });

    it('should set spellcheck="false" on code input', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url: 'https://claude.ai/oauth' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[aria-label="Authorization code"]');

      // Assert
      expect(input?.getAttribute('spellcheck')).toBe('false');
    });

    it('should set autocapitalize="off" on code input', () => {
      // Arrange
      const fixture = setup({ phase: 'WaitingForAuthorization', url: 'https://claude.ai/oauth' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const input = el.querySelector('input[aria-label="Authorization code"]');

      // Assert
      expect(input?.getAttribute('autocapitalize')).toBe('off');
    });
  });
});
