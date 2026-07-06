import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { AccountChipComponent } from './account-chip';
import { SettingsService } from '../../../features/settings/settings.service';
import { AuthSettings } from '../../../features/settings/settings.model';

function createMockSettingsService(authSettings: AuthSettings | null = null) {
  return {
    authSettings: signal(authSettings),
  };
}

function setup(authSettings: AuthSettings | null = null) {
  TestBed.configureTestingModule({
    imports: [AccountChipComponent],
    providers: [
      provideRouter([]),
      { provide: SettingsService, useValue: createMockSettingsService(authSettings) },
    ],
  });
  const fixture = TestBed.createComponent(AccountChipComponent);
  fixture.detectChanges();
  return fixture;
}

describe('AccountChipComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  // Cycle 1a — tracer bullet: renders nothing when authSettings is null (still loading)
  it('should render nothing when authSettings is null', () => {
    // Arrange / Act
    const fixture = setup(null);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('a')).toBeNull();
  });

  // Cycle 1b — OAuth Present renders anchor with email text and routerLink
  it('should render an anchor with email text when oauth status is Present', () => {
    // Arrange
    const authSettings: AuthSettings = {
      mode: 'oauth',
      apiKeyConfigured: false,
      oauth: { status: 'Present', subscriptionType: 'Pro' },
      accountEmail: 'user@example.com',
      accountOrgName: 'Acme Corp',
    };

    // Act
    const fixture = setup(authSettings);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const anchor = el.querySelector('a');
    expect(anchor).not.toBeNull();
    expect(anchor?.textContent).toContain('user@example.com');
  });

  // Cycle 1b (cont.) — OAuth Present: title combines org and subscription
  it('should set title to org name and subscription when both are present', () => {
    // Arrange
    const authSettings: AuthSettings = {
      mode: 'oauth',
      apiKeyConfigured: false,
      oauth: { status: 'Present', subscriptionType: 'Pro' },
      accountEmail: 'user@example.com',
      accountOrgName: 'Acme Corp',
    };

    // Act
    const fixture = setup(authSettings);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const anchor = el.querySelector('a');
    expect(anchor?.getAttribute('title')).toBe('Acme Corp · Pro');
  });

  // Cycle 1c — OAuth ReLoginNeeded: renders warning chip with amber class
  it('should render warning treatment when oauth status is ReLoginNeeded', () => {
    // Arrange
    const authSettings: AuthSettings = {
      mode: 'oauth',
      apiKeyConfigured: false,
      oauth: { status: 'ReLoginNeeded', subscriptionType: null },
      accountEmail: 'user@example.com',
      accountOrgName: null,
    };

    // Act
    const fixture = setup(authSettings);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const anchor = el.querySelector('a');
    expect(anchor).not.toBeNull();
    expect(anchor?.classList.contains('account-chip--warning')).toBe(true);
    expect(anchor?.getAttribute('title')).toBe('Re-login needed');
  });

  // Cycle 1d — api_key mode: renders key icon and "API key" label
  it('should render key icon and "API key" label in api_key mode', () => {
    // Arrange
    const authSettings: AuthSettings = {
      mode: 'api_key',
      apiKeyConfigured: true,
      oauth: null,
      accountEmail: null,
      accountOrgName: null,
    };

    // Act
    const fixture = setup(authSettings);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const anchor = el.querySelector('a');
    expect(anchor).not.toBeNull();
    expect(anchor?.textContent).toContain('API key');
    expect(anchor?.querySelector('svg')).not.toBeNull();
  });

  // Cycle 1e — oauth mode with NotConfigured status renders nothing
  it('should render nothing when oauth mode has status NotConfigured', () => {
    // Arrange
    const authSettings: AuthSettings = {
      mode: 'oauth',
      apiKeyConfigured: false,
      oauth: { status: 'NotConfigured', subscriptionType: null },
      accountEmail: null,
      accountOrgName: null,
    };

    // Act
    const fixture = setup(authSettings);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('a')).toBeNull();
  });

  // Cycle 1f — visible chip has aria-label and routes to /settings
  it('should have aria-label describing worker account on OAuth Present chip', () => {
    // Arrange
    const authSettings: AuthSettings = {
      mode: 'oauth',
      apiKeyConfigured: false,
      oauth: { status: 'Present', subscriptionType: null },
      accountEmail: 'user@example.com',
      accountOrgName: null,
    };

    // Act
    const fixture = setup(authSettings);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const anchor = el.querySelector('a');
    expect(anchor?.getAttribute('aria-label')).toBe('Worker account: user@example.com. Open settings.');
  });

  // Monogram: first letter of email uppercased when OAuth Present
  it('should render monogram with first letter of email uppercased', () => {
    // Arrange
    const authSettings: AuthSettings = {
      mode: 'oauth',
      apiKeyConfigured: false,
      oauth: { status: 'Present', subscriptionType: null },
      accountEmail: 'jane@example.com',
      accountOrgName: null,
    };

    // Act
    const fixture = setup(authSettings);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const monogram = el.querySelector('.account-chip__monogram');
    expect(monogram?.textContent?.trim()).toBe('J');
  });
});
