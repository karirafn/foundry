import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { setupGuard } from './setup.guard';
import { routes } from '../../app.routes';
import { AccountSummary } from '../settings/accounts/account.model';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://api.github.com',
  hasToken: true,
  tokenStatus: 'present',
  branchRulesTokenStatus: 'absent',
  deniedRepositories: [],
  tokenInvalidSince: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const SETTINGS_WITH_URL = { webhookPort: 5099, webhookPublicBaseUrl: 'https://hooks.example.com' };
const SETTINGS_WITHOUT_URL = { webhookPort: 5099, webhookPublicBaseUrl: null };
const SERVER_ERROR = { status: 500, statusText: 'Internal Server Error' };

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });

  return {
    router: TestBed.inject(Router),
    httpMock: TestBed.inject(HttpTestingController),
  };
}

function runGuard(): Promise<boolean | UrlTree> {
  return firstValueFrom(TestBed.runInInjectionContext(() => setupGuard()));
}

describe('setupGuard', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('should redirect to /setup when accounts list is empty', async () => {
    // Arrange
    const { router, httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').flush([]);
    httpMock.expectOne('/api/settings').flush(SETTINGS_WITH_URL);
    const result = await resultPromise;

    // Assert
    expect(result).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(result as UrlTree)).toBe('/setup');
  });

  it('should redirect to /setup when an account exists but no webhook public base URL is stored', async () => {
    // Arrange
    const { router, httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);
    httpMock.expectOne('/api/settings').flush(SETTINGS_WITHOUT_URL);
    const result = await resultPromise;

    // Assert
    expect(result).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(result as UrlTree)).toBe('/setup');
  });

  it('should redirect to /setup when the stored webhook public base URL is empty', async () => {
    // Arrange
    const { router, httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);
    httpMock.expectOne('/api/settings').flush({ ...SETTINGS_WITH_URL, webhookPublicBaseUrl: '' });
    const result = await resultPromise;

    // Assert
    expect(router.serializeUrl(result as UrlTree)).toBe('/setup');
  });

  it('should return true when an account and the webhook public base URL both exist', async () => {
    // Arrange
    const { httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);
    httpMock.expectOne('/api/settings').flush(SETTINGS_WITH_URL);
    const result = await resultPromise;

    // Assert
    expect(result).toBe(true);
  });

  // API is up but erroring — setup is not the problem
  it('should return true when the accounts request returns a server error', async () => {
    // Arrange
    const { httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').flush('Server Error', SERVER_ERROR);
    httpMock.match('/api/settings');
    const result = await resultPromise;

    // Assert
    expect(result).toBe(true);
  });

  it('should return true when the settings request returns a server error', async () => {
    // Arrange
    const { httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);
    httpMock.expectOne('/api/settings').flush('Server Error', SERVER_ERROR);
    const result = await resultPromise;

    // Assert
    expect(result).toBe(true);
  });

  // Network error — a fresh install may not have the API running
  it('should return true when the accounts request fails with a network error', async () => {
    // Arrange
    const { httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').error(new ProgressEvent('network error'));
    httpMock.match('/api/settings');
    const result = await resultPromise;

    // Assert
    expect(result).toBe(true);
  });
});

describe('app routes', () => {
  // Cycle 5: setupGuard protects the settings route
  it('should apply setupGuard to the settings route', () => {
    // Arrange
    const settingsRoute = routes.find((r) => r.path === 'settings');

    // Act
    // (no action — inspecting static route configuration)

    // Assert
    expect(settingsRoute?.canActivate).toBeDefined();
    expect(settingsRoute?.canActivate).toContain(setupGuard);
  });

  it('should open the board at the root instead of redirecting', async () => {
    // Arrange
    const rootRoute = routes.find((r) => r.path === '');

    // Act
    const children = (await rootRoute?.loadChildren?.()) as { path?: string; title?: string }[];

    // Assert
    expect(rootRoute?.redirectTo).toBeUndefined();
    expect(rootRoute?.pathMatch).toBe('full');
    expect(rootRoute?.canActivate).toContain(setupGuard);
    expect(children[0].title).toBe('Board');
  });
});
