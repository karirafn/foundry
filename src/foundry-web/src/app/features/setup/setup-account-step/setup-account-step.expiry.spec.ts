import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TokenValidationResult } from '../../settings/accounts/account.model';
import { SetupAccountStepComponent } from './setup-account-step';

const NOW = new Date(2026, 10, 17, 12, 0);

const VALID_RESULT: TokenValidationResult = {
  kind: 'authenticated',
  missingScopes: [],
  accountName: 'octocat',
  detectedProvider: null,
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

function inDays(days: number): string {
  return new Date(NOW.getFullYear(), NOW.getMonth(), NOW.getDate() + days, 12, 0).toISOString();
}

function text(el: Element | null | undefined): string | undefined {
  return el?.textContent?.replace(/\s+/g, ' ').trim();
}

function checkToken(result: TokenValidationResult): HTMLElement {
  TestBed.configureTestingModule({
    imports: [SetupAccountStepComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  const fixture = TestBed.createComponent(SetupAccountStepComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
  tokenInput.value = 'ghp_token';
  tokenInput.dispatchEvent(new Event('input'));
  tokenInput.dispatchEvent(new Event('blur'));
  fixture.detectChanges();
  httpMock.expectOne('/api/accounts/validate-token').flush(result);
  fixture.detectChanges();
  return el;
}

describe('SetupAccountStepComponent token expiry', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('should show the checked token expiry under "Authenticated as"', () => {
    // Act
    const el = checkToken({ ...VALID_RESULT, tokenExpiry: { status: 'current', expiresAt: inDays(30) } });

    // Assert
    const blocks = el.querySelectorAll('#setup-token-validation .setup-account-step__validation-block');
    expect(text(blocks[1])).toBe('Expires on Dec 17, 2026 (in 30 days)');
  });

  it('should warn that an expired token can be rejected at any time', () => {
    // Act
    const el = checkToken({ ...VALID_RESULT, tokenExpiry: { status: 'expired', expiresAt: inDays(-3) } });

    // Assert
    const blocks = el.querySelectorAll('#setup-token-validation .setup-account-step__validation-block');
    expect(blocks[1].querySelector('.setup-account-step__validation-dot--warning')).toBeTruthy();
    expect(text(blocks[1])).toBe(
      'Warning: Expired on Nov 14, 2026 (3 days ago) — GitHub can reject it at any time; create a new token',
    );
  });

  it('should hide the expiry when it was not checked', () => {
    // Act
    const el = checkToken(VALID_RESULT);

    // Assert
    expect(el.querySelector('#setup-token-validation fd-token-expiry')).toBeNull();
  });

  it('should hide the expiry when scopes are missing', () => {
    // Act
    const el = checkToken({
      ...VALID_RESULT,
      missingScopes: ['repo'],
      tokenExpiry: { status: 'current', expiresAt: inDays(30) },
    });

    // Assert
    expect(el.querySelector('#setup-token-validation fd-token-expiry')).toBeNull();
  });
});
