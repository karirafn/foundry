import { TestBed, ComponentFixture } from '@angular/core/testing';
import { LoginFlowComponent } from './login-flow';
import { LoginError, LoginPhase } from '../../../../core/models/settings.model';

function setup(inputs: {
  phase: LoginPhase;
  url?: string | null;
  error?: LoginError | null;
  accountEmail?: string | null;
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
  fixture.detectChanges();
  return fixture;
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
