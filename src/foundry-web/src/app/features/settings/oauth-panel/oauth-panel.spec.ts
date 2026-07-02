import { TestBed, ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { OAuthPanelComponent } from './oauth-panel';

function setup(inputs: {
  status: 'NotConfigured' | 'Present' | 'ReLoginNeeded';
  expiresAt?: string | null;
  subscriptionType?: string | null;
  loginCommand?: string | null;
  loginCommandLoading?: boolean;
  loginCommandError?: string | null;
}): ComponentFixture<OAuthPanelComponent> {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [OAuthPanelComponent],
  });
  const fixture = TestBed.createComponent(OAuthPanelComponent);
  fixture.componentRef.setInput('status', inputs.status);
  fixture.componentRef.setInput('expiresAt', inputs.expiresAt ?? null);
  fixture.componentRef.setInput('subscriptionType', inputs.subscriptionType ?? null);
  fixture.componentRef.setInput('loginCommand', inputs.loginCommand ?? null);
  fixture.componentRef.setInput('loginCommandLoading', inputs.loginCommandLoading ?? false);
  fixture.componentRef.setInput('loginCommandError', inputs.loginCommandError ?? null);
  fixture.detectChanges();
  return fixture;
}

describe('OAuthPanelComponent', () => {
  // Present state
  describe('when status is Present', () => {
    it('should show "Signed in" badge text', () => {
      // Arrange
      const fixture = setup({ status: 'Present', expiresAt: '2027-01-01T00:00:00Z', subscriptionType: 'pro' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const badge = el.querySelector('.oauth-panel__badge');

      // Assert
      expect(badge?.textContent?.trim()).toContain('Signed in');
    });

    it('should apply success modifier to badge when status is Present', () => {
      // Arrange
      const fixture = setup({ status: 'Present' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const badge = el.querySelector('.oauth-panel__badge--success');

      // Assert
      expect(badge).toBeTruthy();
    });

    it('should show subscriptionType when provided', () => {
      // Arrange
      const fixture = setup({ status: 'Present', subscriptionType: 'pro' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('pro');
    });

    it('should show "Claude account" when subscriptionType is null', () => {
      // Arrange
      const fixture = setup({ status: 'Present', subscriptionType: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Claude account');
    });

    it('should show expires hint', () => {
      // Arrange
      const fixture = setup({ status: 'Present', expiresAt: '2027-01-01T00:00:00Z' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Access token expires');
    });

    it('should show "—" when expiresAt is null', () => {
      // Arrange
      const fixture = setup({ status: 'Present', expiresAt: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const expiresRow = el.querySelector('.oauth-panel__expires-value');

      // Assert
      expect(expiresRow?.textContent?.trim()).toBe('—');
    });

    it('should show the auto-refresh hint', () => {
      // Arrange
      const fixture = setup({ status: 'Present' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Claude Code refreshes this token automatically');
    });

    it('should NOT show the login command block when status is Present', () => {
      // Arrange
      const fixture = setup({ status: 'Present', loginCommand: 'docker run -it' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const commandBlock = el.querySelector('.oauth-panel__command-block');

      // Assert
      expect(commandBlock).toBeFalsy();
    });

    it('should NOT render the word "valid" anywhere', () => {
      // Arrange
      const fixture = setup({ status: 'Present' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent?.toLowerCase()).not.toContain('valid');
    });
  });

  // ReLoginNeeded state
  describe('when status is ReLoginNeeded', () => {
    it('should show "Re-login needed" badge text', () => {
      // Arrange
      const fixture = setup({ status: 'ReLoginNeeded' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const badge = el.querySelector('.oauth-panel__badge');

      // Assert
      expect(badge?.textContent?.trim()).toContain('Re-login needed');
    });

    it('should apply warning modifier to badge when status is ReLoginNeeded', () => {
      // Arrange
      const fixture = setup({ status: 'ReLoginNeeded' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const badge = el.querySelector('.oauth-panel__badge--warning');

      // Assert
      expect(badge).toBeTruthy();
    });

    it('should show credential refresh message', () => {
      // Arrange
      const fixture = setup({ status: 'ReLoginNeeded' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Your credential needs a refresh');
    });

    it('should show the login command block when status is ReLoginNeeded', () => {
      // Arrange
      const fixture = setup({ status: 'ReLoginNeeded', loginCommand: 'docker run -it --rm claude /login' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const commandBlock = el.querySelector('.oauth-panel__command-block');

      // Assert
      expect(commandBlock).toBeTruthy();
    });

    it('should render the login command in a pre element', () => {
      // Arrange
      const fixture = setup({ status: 'ReLoginNeeded', loginCommand: 'docker run -it --rm claude /login' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const pre = el.querySelector('pre[aria-label="OAuth login command"]');

      // Assert
      expect(pre).toBeTruthy();
      expect(pre?.textContent).toContain('docker run -it --rm claude /login');
    });

    it('should render the Copy button with aria-label "Copy login command"', () => {
      // Arrange
      const fixture = setup({ status: 'ReLoginNeeded', loginCommand: 'docker run -it --rm claude /login' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const copyBtn = el.querySelector('button[aria-label="Copy login command"]');

      // Assert
      expect(copyBtn).toBeTruthy();
    });

    it('should render the refresh button', () => {
      // Arrange
      const fixture = setup({ status: 'ReLoginNeeded', loginCommand: 'docker run -it --rm claude /login' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const refreshBtn = el.querySelector('.oauth-panel__refresh-btn');

      // Assert
      expect(refreshBtn).toBeTruthy();
      expect(refreshBtn?.textContent).toContain("I've logged in");
    });
  });

  // NotConfigured state
  describe('when status is NotConfigured', () => {
    it('should show "Not configured" badge text', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const badge = el.querySelector('.oauth-panel__badge');

      // Assert
      expect(badge?.textContent?.trim()).toContain('Not configured');
    });

    it('should apply error modifier to badge when status is NotConfigured', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const badge = el.querySelector('.oauth-panel__badge--error');

      // Assert
      expect(badge).toBeTruthy();
    });

    it('should show guidance to run the login command', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Run this command in your terminal to sign in');
    });

    it('should show the login command block when status is NotConfigured', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommand: 'docker run -it --rm claude /login' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const commandBlock = el.querySelector('.oauth-panel__command-block');

      // Assert
      expect(commandBlock).toBeTruthy();
    });
  });

  // Loading state
  describe('login command loading', () => {
    it('should show loading spinner and message when loginCommandLoading is true', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommandLoading: true });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const status = el.querySelector('[role="status"]');

      // Assert
      expect(status?.textContent).toContain('Preparing login command');
    });

    it('should set aria-live="polite" on the loading status region', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommandLoading: true });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('[role="status"][aria-live="polite"]');

      // Assert
      expect(statusEl).toBeTruthy();
    });
  });

  // Error state
  describe('login command error', () => {
    it('should show error message with role="alert" when loginCommandError is set', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommandError: 'Fetch failed' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const alert = el.querySelector('[role="alert"]');

      // Assert
      expect(alert?.textContent).toContain("Couldn't load the login command");
    });

    it('should show a Retry affordance when loginCommandError is set', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommandError: 'Fetch failed' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.oauth-panel__retry-command-btn');

      // Assert
      expect(retryBtn).toBeTruthy();
    });
  });

  // Refresh output
  describe('refresh output', () => {
    it('should emit refresh when the "I\'ve logged in" button is clicked', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommand: 'docker run -it' });
      let emitted = false;
      fixture.componentInstance.refresh.subscribe(() => (emitted = true));

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const refreshBtn = el.querySelector('.oauth-panel__refresh-btn') as HTMLButtonElement;
      refreshBtn.click();

      // Assert
      expect(emitted).toBe(true);
    });

    it('should emit fetchCommand when the Retry button is clicked', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommandError: 'err' });
      let emitted = false;
      fixture.componentInstance.fetchCommand.subscribe(() => (emitted = true));

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.oauth-panel__retry-command-btn') as HTMLButtonElement;
      retryBtn.click();

      // Assert
      expect(emitted).toBe(true);
    });
  });

  // Copy button
  describe('copy button', () => {
    it('should show "Copy" label initially', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommand: 'docker run -it' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const copyBtn = el.querySelector('button[aria-label="Copy login command"]') as HTMLButtonElement;

      // Assert
      expect(copyBtn?.textContent?.trim()).toContain('Copy');
    });
  });

  // Accessibility: pre element
  describe('pre element accessibility', () => {
    it('should have tabindex="0" and aria-label on the pre element', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommand: 'docker run -it' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const pre = el.querySelector('pre');

      // Assert
      expect(pre?.getAttribute('tabindex')).toBe('0');
      expect(pre?.getAttribute('aria-label')).toBe('OAuth login command');
    });
  });

  // aria-live region for copy success
  describe('copy success announcement', () => {
    it('should have an aria-live="polite" region for copy success announcements', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginCommand: 'docker run -it' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const liveRegion = el.querySelector('.oauth-panel__copy-announcement[aria-live="polite"]');

      // Assert
      expect(liveRegion).toBeTruthy();
    });
  });
});
