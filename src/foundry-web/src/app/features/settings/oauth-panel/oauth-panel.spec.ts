import { TestBed, ComponentFixture } from '@angular/core/testing';
import { OAuthPanelComponent } from './oauth-panel';
import { LoginError, LoginPhase } from '../../../core/models/settings.model';

function setup(inputs: {
  status: 'NotConfigured' | 'Present' | 'ReLoginNeeded';
  subscriptionType?: string | null;
  accountEmail?: string | null;
  accountOrgName?: string | null;
  loginPhase?: LoginPhase | null;
  loginUrl?: string | null;
  loginError?: LoginError | null;
}): ComponentFixture<OAuthPanelComponent> {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [OAuthPanelComponent],
  });
  const fixture = TestBed.createComponent(OAuthPanelComponent);
  fixture.componentRef.setInput('status', inputs.status);
  fixture.componentRef.setInput('subscriptionType', inputs.subscriptionType ?? null);
  fixture.componentRef.setInput('accountEmail', inputs.accountEmail ?? null);
  fixture.componentRef.setInput('accountOrgName', inputs.accountOrgName ?? null);
  fixture.componentRef.setInput('loginPhase', inputs.loginPhase ?? null);
  fixture.componentRef.setInput('loginUrl', inputs.loginUrl ?? null);
  fixture.componentRef.setInput('loginError', inputs.loginError ?? null);
  fixture.detectChanges();
  return fixture;
}

describe('OAuthPanelComponent', () => {
  // Badge
  describe('badge', () => {
    it('should show "Signed in" badge when status is Present', () => {
      // Arrange
      const fixture = setup({ status: 'Present' });

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

      // Assert
      expect(el.querySelector('.oauth-panel__badge--success')).toBeTruthy();
    });

    it('should show "Re-login needed" badge when status is ReLoginNeeded', () => {
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

      // Assert
      expect(el.querySelector('.oauth-panel__badge--warning')).toBeTruthy();
    });

    it('should show "Not configured" badge when status is NotConfigured', () => {
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

      // Assert
      expect(el.querySelector('.oauth-panel__badge--error')).toBeTruthy();
    });
  });

  // Present card
  describe('Present card', () => {
    it('should show account email row', () => {
      // Arrange
      const fixture = setup({ status: 'Present', accountEmail: 'user@example.com' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Account');
      expect(el.textContent).toContain('user@example.com');
    });

    it('should show "Claude account" fallback when accountEmail is null', () => {
      // Arrange
      const fixture = setup({ status: 'Present', accountEmail: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Claude account');
    });

    it('should show Organization row when accountOrgName is set', () => {
      // Arrange
      const fixture = setup({ status: 'Present', accountOrgName: 'Acme Corp' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Organization');
      expect(el.textContent).toContain('Acme Corp');
    });

    it('should NOT show Organization row when accountOrgName is null', () => {
      // Arrange
      const fixture = setup({ status: 'Present', accountOrgName: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).not.toContain('Organization');
    });

    it('should show Plan row with subscriptionType', () => {
      // Arrange
      const fixture = setup({ status: 'Present', subscriptionType: 'pro' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Plan');
      expect(el.textContent).toContain('pro');
    });

    it('should show "Switch account" button when status is Present and loginPhase is null', () => {
      // Arrange
      const fixture = setup({ status: 'Present', loginPhase: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const switchBtn = el.querySelector('.oauth-panel__switch-btn');

      // Assert
      expect(switchBtn).toBeTruthy();
      expect(switchBtn?.textContent?.trim()).toBe('Switch account');
    });

    it('should emit startLogin when Switch account is clicked', () => {
      // Arrange
      const fixture = setup({ status: 'Present', loginPhase: null });
      let emitted = false;
      fixture.componentInstance.startLogin.subscribe(() => (emitted = true));

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
      switchBtn.click();

      // Assert
      expect(emitted).toBe(true);
    });
  });

  // NotConfigured entry
  describe('NotConfigured entry', () => {
    it('should show entry message for NotConfigured', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginPhase: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('No account is signed in yet');
    });

    it('should show "Log in" button in NotConfigured state when loginPhase is null', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginPhase: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const logInBtn = el.querySelector('.oauth-panel__login-btn');

      // Assert
      expect(logInBtn).toBeTruthy();
      expect(logInBtn?.textContent?.trim()).toBe('Log in');
    });

    it('should emit startLogin when Log in button is clicked', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginPhase: null });
      let emitted = false;
      fixture.componentInstance.startLogin.subscribe(() => (emitted = true));

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const logInBtn = el.querySelector('.oauth-panel__login-btn') as HTMLButtonElement;
      logInBtn.click();

      // Assert
      expect(emitted).toBe(true);
    });
  });

  // ReLoginNeeded entry
  describe('ReLoginNeeded entry', () => {
    it('should show entry message for ReLoginNeeded', () => {
      // Arrange
      const fixture = setup({ status: 'ReLoginNeeded', loginPhase: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Your saved credential expired');
    });

    it('should show "Log in again" button in ReLoginNeeded state when loginPhase is null', () => {
      // Arrange
      const fixture = setup({ status: 'ReLoginNeeded', loginPhase: null });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const logInBtn = el.querySelector('.oauth-panel__login-btn');

      // Assert
      expect(logInBtn).toBeTruthy();
      expect(logInBtn?.textContent?.trim()).toBe('Log in again');
    });
  });

  // Login flow embedding
  describe('login flow embedding', () => {
    it('should show fd-oauth-login-flow when loginPhase is Starting', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginPhase: 'Starting' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const loginFlow = el.querySelector('fd-oauth-login-flow');

      // Assert
      expect(loginFlow).toBeTruthy();
    });

    it('should show fd-oauth-login-flow when loginPhase is WaitingForAuthorization', () => {
      // Arrange
      const fixture = setup({
        status: 'NotConfigured',
        loginPhase: 'WaitingForAuthorization',
        loginUrl: 'https://claude.ai/oauth',
      });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const loginFlow = el.querySelector('fd-oauth-login-flow');

      // Assert
      expect(loginFlow).toBeTruthy();
    });

    it('should NOT show login entry button when loginPhase is active', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginPhase: 'Starting' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const logInBtn = el.querySelector('.oauth-panel__login-btn');

      // Assert
      expect(logInBtn).toBeFalsy();
    });

    it('should emit submitCode when login flow emits submitCode', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured', loginPhase: 'WaitingForAuthorization', loginUrl: 'https://claude.ai' });
      let emittedCode: string | undefined;
      fixture.componentInstance.submitCode.subscribe((code: string) => (emittedCode = code));

      // Act — manually call the handler as child component interaction is tested in child spec
      fixture.componentInstance.onSubmitCode('test-code');

      // Assert
      expect(emittedCode).toBe('test-code');
    });
  });

  // No command block (old copy-paste path removed)
  describe('no legacy command block', () => {
    it('should NOT render the command-pre element', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured' });

      // Act
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.oauth-panel__command-pre')).toBeFalsy();
    });

    it('should NOT render a Copy button', () => {
      // Arrange
      const fixture = setup({ status: 'NotConfigured' });

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const buttons = el.querySelectorAll('button');
      const copyBtn = Array.from(buttons).find(b => b.textContent?.trim() === 'Copy');

      // Assert
      expect(copyBtn).toBeFalsy();
    });
  });
});
