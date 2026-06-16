import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { setupGuard } from './setup.guard';
import { AccountSummary } from '../settings/accounts/account.model';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://api.github.com',
  hasToken: true,
};

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

  // Cycle 1: redirects to /setup when accounts list is empty
  it('should redirect to /setup when accounts list is empty', async () => {
    // Arrange
    const { router, httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').flush([]);
    const result = await resultPromise;

    // Assert
    expect(result).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(result as UrlTree)).toBe('/setup');
  });

  // Cycle 2: allows navigation when accounts exist
  it('should return true when accounts exist', async () => {
    // Arrange
    const { httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);
    const result = await resultPromise;

    // Assert
    expect(result).toBe(true);
  });

  // Cycle 3: handles HTTP error gracefully (allow navigation on error)
  it('should return true when the accounts request fails', async () => {
    // Arrange
    const { httpMock } = setup();

    // Act
    const resultPromise = runGuard();
    httpMock.expectOne('/api/accounts').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    const result = await resultPromise;

    // Assert
    expect(result).toBe(true);
  });
});
