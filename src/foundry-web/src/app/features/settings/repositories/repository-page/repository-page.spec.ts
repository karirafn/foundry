import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { RepositoryPageComponent } from './repository-page';
import { RepositoryService } from '../repository.service';
import { AccountService } from '../../accounts/account.service';
import { RepositorySummary, UpdateRepositoryRequest } from '../repository.model';
import { AccountSummary } from '../../accounts/account.model';

const ACCOUNT_1: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  tokenStatus: 'present',
  namespaces: [],
};

const REPO_1: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000010',
  slug: 'my-org/my-repo',
  accountId: ACCOUNT_1.id,
  accountName: ACCOUNT_1.name,
  providerType: 'github',
  position: 0,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: true,
  maxConcurrentWorkers: 1,
  lastPolledAt: '2026-06-15T10:00:00Z',
  eligibility: { status: 'eligible', violations: [], reason: null },
};

const REPO_1_INELIGIBLE: RepositorySummary = {
  ...REPO_1,
  id: '00000000-0000-0000-0000-000000000011',
  slug: 'my-org/ineligible-repo',
  eligibility: {
    status: 'ineligible',
    violations: [{ rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' }],
    reason: null,
  },
};

function setup(repositoryId: string = REPO_1.id) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [RepositoryPageComponent],
    providers: [
      AccountService,
      RepositoryService,
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: ActivatedRoute,
        useValue: {
          paramMap: of(convertToParamMap({ repositoryId })),
        },
      },
    ],
  });

  const fixture = TestBed.createComponent(RepositoryPageComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  const repositoryService = TestBed.inject(RepositoryService);
  const component = fixture.componentInstance;

  return { fixture, httpMock, repositoryService, component };
}

function flushAccounts(httpMock: HttpTestingController, accounts: AccountSummary[] = []): void {
  httpMock.expectOne('/api/accounts').flush(accounts);
}

function seedRepositories(repositoryService: RepositoryService, repos: RepositorySummary[], component: RepositoryPageComponent): void {
  // Directly set signal state for unit tests — bypasses HTTP
  (repositoryService as unknown as { _repositoriesSignal: { set: (v: RepositorySummary[]) => void } })
    ._repositoriesSignal.set(repos);
  (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
    ._loadingSignal.set(false);
  (component as unknown as { _loadAttempted: { set: (v: boolean) => void } })
    ._loadAttempted.set(true);
}

describe('RepositoryPageComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
  });

  describe('loaded state', () => {
    it('should render fd-repository-form when repository is found in service cache', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const form = el.querySelector('fd-repository-form');
      expect(form).toBeTruthy();
    });

    it('should render fd-repository-eligibility-details when eligibility is ineligible', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1_INELIGIBLE.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1_INELIGIBLE], component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const details = el.querySelector('fd-repository-eligibility-details');
      expect(details).toBeTruthy();
    });

    it('should wrap fd-repository-eligibility-details in a section with h2 "Eligibility"', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1_INELIGIBLE.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1_INELIGIBLE], component);

      // Act
      fixture.detectChanges();

      // Assert — eligibility section with h2
      const el = fixture.nativeElement as HTMLElement;
      const eligibilitySection = el.querySelector('.repository-page__eligibility');
      expect(eligibilitySection?.tagName.toLowerCase()).toBe('section');
      const heading = eligibilitySection?.querySelector('.repository-page__eligibility-heading');
      expect(heading?.tagName.toLowerCase()).toBe('h2');
      expect(heading?.textContent?.trim()).toBe('Eligibility');
    });

    it('should not render fd-repository-eligibility-details when eligibility is eligible', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const details = el.querySelector('fd-repository-eligibility-details');
      expect(details).toBeFalsy();
    });

    it('should display the repository slug in the page heading', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const heading = el.querySelector('.repository-page__heading');
      expect(heading?.textContent?.trim()).toBe(REPO_1.slug);
    });
  });

  describe('aria-labelledby', () => {
    it('should set aria-labelledby to "repository-page-heading" in loaded state', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const section = el.querySelector('.repository-page__section');
      expect(section?.getAttribute('aria-labelledby')).toBe('repository-page-heading');
    });

    it('should omit aria-labelledby in loading state', () => {
      // Arrange
      const { fixture, httpMock } = setup(REPO_1.id);

      // Act
      fixture.detectChanges();

      // Assert — loading state: no aria-labelledby
      const el = fixture.nativeElement as HTMLElement;
      const section = el.querySelector('.repository-page__section');
      expect(section?.getAttribute('aria-labelledby')).toBeNull();

      // Cleanup
      flushAccounts(httpMock);
    });

    it('should omit aria-labelledby in load-error state', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      (repositoryService as unknown as { _loadErrorSignal: { set: (v: string | null) => void } })
        ._loadErrorSignal.set('Network error');
      (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
        ._loadingSignal.set(false);
      (component as unknown as { _loadAttempted: { set: (v: boolean) => void } })
        ._loadAttempted.set(true);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const section = el.querySelector('.repository-page__section');
      expect(section?.getAttribute('aria-labelledby')).toBeNull();
    });

    it('should omit aria-labelledby in not-found state', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup('00000000-0000-0000-0000-000000000999');
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const section = el.querySelector('.repository-page__section');
      expect(section?.getAttribute('aria-labelledby')).toBeNull();
    });
  });

  describe('not-found state', () => {
    it('should render not-found heading as an h1 element', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup('00000000-0000-0000-0000-000000000999');
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const heading = el.querySelector('.repository-page__not-found-heading');
      expect(heading?.tagName.toLowerCase()).toBe('h1');
      expect(heading?.textContent?.trim()).toBe('Repository not found');
    });

    it('should render not-found message when load settled and id absent', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup('00000000-0000-0000-0000-000000000999');
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const notFound = el.querySelector('.repository-page__not-found');
      expect(notFound).toBeTruthy();
      const form = el.querySelector('fd-repository-form');
      expect(form).toBeFalsy();
    });

    it('should render back-to-list link in not-found state', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup('00000000-0000-0000-0000-000000000999');
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const backLink = el.querySelector('.repository-page__back-link');
      expect(backLink).toBeTruthy();
    });
  });

  describe('load-error state', () => {
    function seedLoadError(repositoryService: RepositoryService, message: string, component: RepositoryPageComponent): void {
      (repositoryService as unknown as { _loadErrorSignal: { set: (v: string | null) => void } })
        ._loadErrorSignal.set(message);
      (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
        ._loadingSignal.set(false);
      (component as unknown as { _loadAttempted: { set: (v: boolean) => void } })
        ._loadAttempted.set(true);
    }

    it('should render load-error block when loadError signal is set', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedLoadError(repositoryService, 'Network error', component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const errorBlock = el.querySelector('.repository-page__load-error');
      expect(errorBlock).toBeTruthy();
      const form = el.querySelector('fd-repository-form');
      expect(form).toBeFalsy();
    });

    it('should have role="alert" on the load-error block', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedLoadError(repositoryService, 'Network error', component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const errorBlock = el.querySelector('.repository-page__load-error');
      expect(errorBlock?.getAttribute('role')).toBe('alert');
    });

    it('should always render the friendly error copy regardless of raw service message', () => {
      // Arrange — seed a raw service error (e.g. HTTP status string) that must not be exposed
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedLoadError(repositoryService, 'Http failure response for https://api.example.com/: 500 Internal Server Error', component);

      // Act
      fixture.detectChanges();

      // Assert — friendly copy is shown, not the raw service message
      const el = fixture.nativeElement as HTMLElement;
      const message = el.querySelector('.repository-page__load-error-message');
      expect(message?.textContent?.trim()).toBe("Couldn't load repositories. Check your connection and try again.");
    });

    it('should render a Retry button in the load-error block', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedLoadError(repositoryService, 'Network error', component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.repository-page__retry-btn');
      expect(retryBtn).toBeTruthy();
      expect(retryBtn?.tagName.toLowerCase()).toBe('button');
      expect(retryBtn?.textContent?.trim()).toBe('Retry');
    });

    it('should render a back-to-repositories link in the load-error block', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedLoadError(repositoryService, 'Network error', component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const backLink = el.querySelector('.repository-page__load-error .repository-page__back-link');
      expect(backLink).toBeTruthy();
      expect(backLink?.textContent?.trim()).toBe('Back to repositories');
    });

    it('should re-trigger loadAllRepositories when Retry button is clicked', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      httpMock.expectOne('/api/accounts').flush([ACCOUNT_1]);
      fixture.detectChanges();
      // The account-keyed effect fires the first load; flush it
      httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`).flush([], { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      // Act — click Retry
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector<HTMLButtonElement>('.repository-page__retry-btn');
      retryBtn?.click();
      fixture.detectChanges();

      // Assert — a new repositories request fires
      const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`);
      expect(req.request.method).toBe('GET');
      req.flush([REPO_1]);
    });

    it('should announce "Could not load repositories" in the status live region on load-error', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedLoadError(repositoryService, 'Network error', component);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const announcer = el.querySelector('[role="status"][aria-live="polite"]');
      expect(announcer?.textContent?.trim()).toBe('Could not load repositories');
    });
  });

  describe('focus-on-load', () => {
    it('should move focus to the not-found heading when not-found state is reached', async () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup('00000000-0000-0000-0000-000000000999');
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);

      // Act
      fixture.detectChanges();
      await fixture.whenStable();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const heading = el.querySelector('.repository-page__not-found-heading') as HTMLElement;
      expect(document.activeElement).toBe(heading);
    });

    it('should move focus to the load-error message when load-error state is reached', async () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      (repositoryService as unknown as { _loadErrorSignal: { set: (v: string | null) => void } })
        ._loadErrorSignal.set('Network error');
      (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
        ._loadingSignal.set(false);
      (component as unknown as { _loadAttempted: { set: (v: boolean) => void } })
        ._loadAttempted.set(true);

      // Act
      fixture.detectChanges();
      await fixture.whenStable();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const message = el.querySelector('.repository-page__load-error-message') as HTMLElement;
      expect(document.activeElement).toBe(message);
    });
  });

  describe('loading state', () => {
    it('should render loading indicator while repositories are loading', () => {
      // Arrange
      const { fixture, repositoryService, httpMock } = setup(REPO_1.id);
      (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
        ._loadingSignal.set(true);

      // Act
      fixture.detectChanges();
      flushAccounts(httpMock);

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const loading = el.querySelector('.repository-page__loading');
      expect(loading).toBeTruthy();
      const form = el.querySelector('fd-repository-form');
      expect(form).toBeFalsy();
    });

    it('should not show not-found while loading (guards against premature not-found flash)', () => {
      // Arrange
      const { fixture, repositoryService, httpMock } = setup('00000000-0000-0000-0000-000000000999');
      (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
        ._loadingSignal.set(true);

      // Act
      fixture.detectChanges();
      flushAccounts(httpMock);

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const notFound = el.querySelector('.repository-page__not-found');
      expect(notFound).toBeFalsy();
    });

    it('should show loading indicator before any load has been attempted (guards cold-load not-found flash)', () => {
      // Arrange — service starts with loading=false and repositories=[] (default initial state)
      // _loadAttempted is false by default; page shows loading until load has been attempted.
      const { fixture, httpMock } = setup(REPO_1.id);

      // Act — detect changes triggers ngOnInit which fires loadAccounts
      fixture.detectChanges();

      // Assert — before flushing accounts, the page is in loading state (_loadAttempted=false)
      const el = fixture.nativeElement as HTMLElement;
      const loading = el.querySelector('.repository-page__loading');
      expect(loading).toBeTruthy();
      const notFound = el.querySelector('.repository-page__not-found');
      expect(notFound).toBeFalsy();

      // Cleanup — flush the accounts request to satisfy afterEach verify
      flushAccounts(httpMock);
    });
  });

  describe('cold load', () => {
    it('should call loadAccounts on init', () => {
      // Arrange
      const { fixture, httpMock } = setup(REPO_1.id);

      // Act
      fixture.detectChanges();

      // Assert — accounts endpoint is called
      const req = httpMock.expectOne('/api/accounts');
      req.flush([]);
    });

    it('should call loadAllRepositories when accounts load', () => {
      // Arrange
      const { fixture, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();

      // Act — flush accounts response
      httpMock.expectOne('/api/accounts').flush([ACCOUNT_1]);
      fixture.detectChanges();

      // Assert — repositories are loaded for account 1
      const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`);
      req.flush([REPO_1]);
    });

    it('should resolve repository and show loaded state after cold load completes', () => {
      // Arrange
      const { fixture, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();

      // Act
      httpMock.expectOne('/api/accounts').flush([ACCOUNT_1]);
      fixture.detectChanges();
      httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`).flush([REPO_1]);
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const form = el.querySelector('fd-repository-form');
      expect(form).toBeTruthy();
    });

    it('should not re-trigger loadAllRepositories when accounts have not changed', () => {
      // Arrange
      const { fixture, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      httpMock.expectOne('/api/accounts').flush([ACCOUNT_1]);
      fixture.detectChanges();
      httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`).flush([REPO_1]);
      fixture.detectChanges();

      // Act — trigger another detectChanges
      fixture.detectChanges();

      // Assert — no extra repository requests
      httpMock.expectNone(`/api/accounts/${ACCOUNT_1.id}/repositories`);
    });
  });

  describe('back navigation', () => {
    it('should navigate to /settings/repositories when onBack is called', () => {
      // Arrange
      const { fixture, repositoryService, component, httpMock } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);
      fixture.detectChanges();
      const router = TestBed.inject(Router);
      const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

      // Act
      component.onBack();

      // Assert
      expect(navigateSpy).toHaveBeenCalledWith(['/settings/repositories']);
    });
  });

  describe('save', () => {
    it('should call updateRepository with the correct accountId, id, and request when onSave is called', () => {
      // Arrange
      const { fixture, repositoryService, httpMock, component } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);
      fixture.detectChanges();
      const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: false, maxConcurrentWorkers: 2 };

      // Act
      component.onSave(request);

      // Assert
      const req = httpMock.expectOne(`/api/accounts/${REPO_1.accountId}/repositories/${REPO_1.id}`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      req.flush({ ...REPO_1, ...request });
    });

    it('should update the displayed repository via computed when save succeeds', () => {
      // Arrange
      const { fixture, repositoryService, httpMock, component } = setup(REPO_1.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1], component);
      fixture.detectChanges();
      const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: false, maxConcurrentWorkers: 2 };

      // Act
      component.onSave(request);
      const updatedRepo: RepositorySummary = { ...REPO_1, pollIntervalSeconds: 600, isActive: false, maxConcurrentWorkers: 2 };
      httpMock.expectOne(`/api/accounts/${REPO_1.accountId}/repositories/${REPO_1.id}`).flush(updatedRepo);
      fixture.detectChanges();

      // Assert — the repository computed reflects the updated entry from service
      expect(component['_repository']()?.pollIntervalSeconds).toBe(600);
      expect(component['_repository']()?.isActive).toBe(false);
    });
  });

  describe('recheck', () => {
    it('should call recheckEligibility with the correct accountId and id when onRecheck is called', () => {
      // Arrange
      const { fixture, repositoryService, httpMock, component } = setup(REPO_1_INELIGIBLE.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1_INELIGIBLE], component);
      fixture.detectChanges();

      // Act
      component.onRecheck();

      // Assert
      const req = httpMock.expectOne(`/api/accounts/${REPO_1_INELIGIBLE.accountId}/repositories/${REPO_1_INELIGIBLE.id}/recheck`);
      expect(req.request.method).toBe('POST');
      req.flush(REPO_1_INELIGIBLE);
    });

    it('should set recheckPending to true while recheck is in flight', () => {
      // Arrange
      const { fixture, repositoryService, httpMock, component } = setup(REPO_1_INELIGIBLE.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1_INELIGIBLE], component);
      fixture.detectChanges();

      // Act
      component.onRecheck();
      fixture.detectChanges();

      // Assert
      expect(component['_recheckPending']()).toBe(true);
      httpMock.expectOne(`/api/accounts/${REPO_1_INELIGIBLE.accountId}/repositories/${REPO_1_INELIGIBLE.id}/recheck`).flush(REPO_1_INELIGIBLE);
    });

    it('should clear recheckPending after recheck succeeds', () => {
      // Arrange
      const { fixture, repositoryService, httpMock, component } = setup(REPO_1_INELIGIBLE.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1_INELIGIBLE], component);
      fixture.detectChanges();

      // Act
      component.onRecheck();
      httpMock.expectOne(`/api/accounts/${REPO_1_INELIGIBLE.accountId}/repositories/${REPO_1_INELIGIBLE.id}/recheck`).flush(REPO_1_INELIGIBLE);
      fixture.detectChanges();

      // Assert
      expect(component['_recheckPending']()).toBe(false);
    });

    it('should set recheckError when recheck fails', () => {
      // Arrange
      const { fixture, repositoryService, httpMock, component } = setup(REPO_1_INELIGIBLE.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1_INELIGIBLE], component);
      fixture.detectChanges();

      // Act
      component.onRecheck();
      httpMock
        .expectOne(`/api/accounts/${REPO_1_INELIGIBLE.accountId}/repositories/${REPO_1_INELIGIBLE.id}/recheck`)
        .flush('Server error', { status: 500, statusText: 'Server error' });
      fixture.detectChanges();

      // Assert
      expect(component['_recheckPending']()).toBe(false);
      expect(component['_recheckError']()).not.toBeNull();
    });

    it('should clear recheckError on subsequent successful recheck', () => {
      // Arrange
      const { fixture, repositoryService, httpMock, component } = setup(REPO_1_INELIGIBLE.id);
      fixture.detectChanges();
      flushAccounts(httpMock);
      seedRepositories(repositoryService, [REPO_1_INELIGIBLE], component);
      fixture.detectChanges();

      // First recheck fails
      component.onRecheck();
      httpMock
        .expectOne(`/api/accounts/${REPO_1_INELIGIBLE.accountId}/repositories/${REPO_1_INELIGIBLE.id}/recheck`)
        .flush('Server error', { status: 500, statusText: 'Server error' });
      fixture.detectChanges();

      // Act — second recheck succeeds
      component.onRecheck();
      httpMock
        .expectOne(`/api/accounts/${REPO_1_INELIGIBLE.accountId}/repositories/${REPO_1_INELIGIBLE.id}/recheck`)
        .flush(REPO_1_INELIGIBLE);
      fixture.detectChanges();

      // Assert
      expect(component['_recheckError']()).toBeNull();
    });
  });

  describe('routed smoke (RouterTestingHarness — pre-ship integration layer)', () => {
    function setupRoutedHarness() {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          AccountService,
          RepositoryService,
          provideHttpClient(),
          provideHttpClientTesting(),
          provideRouter([
            {
              path: 'repositories/:repositoryId',
              component: RepositoryPageComponent,
            },
          ]),
        ],
      });
    }

    it('should render fd-repository-form when navigating to a known repository id', async () => {
      // Arrange
      setupRoutedHarness();
      const httpMock = TestBed.inject(HttpTestingController);

      // Act — navigate to the known repository id via the real router
      const harness = await RouterTestingHarness.create(`/repositories/${REPO_1.id}`);
      // Flush accounts first; this triggers the account-keyed effect in the page
      httpMock.expectOne('/api/accounts').flush([ACCOUNT_1]);
      harness.detectChanges();
      await harness.fixture.whenStable();
      // Now flush repositories — accounts resolved, loadAllRepositories fired
      httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`).flush([REPO_1]);
      harness.detectChanges();
      await harness.fixture.whenStable();
      harness.detectChanges();

      // Assert — form is rendered for the found repository
      const el = harness.fixture.nativeElement as HTMLElement;
      const form = el.querySelector('fd-repository-form');
      expect(form).toBeTruthy();
      const heading = el.querySelector('.repository-page__heading');
      expect(heading?.textContent?.trim()).toBe(REPO_1.slug);

      httpMock.verify();
    });

    it('should render the not-found state when navigating to an unknown repository id', async () => {
      // Arrange
      setupRoutedHarness();
      const httpMock = TestBed.inject(HttpTestingController);
      const unknownId = '00000000-0000-0000-0000-000000000999';

      // Act — navigate to an id that does not exist in the service cache
      const harness = await RouterTestingHarness.create(`/repositories/${unknownId}`);
      // Flush accounts first; this triggers the account-keyed effect in the page
      httpMock.expectOne('/api/accounts').flush([ACCOUNT_1]);
      harness.detectChanges();
      await harness.fixture.whenStable();
      // Flush repositories — the requested id is absent, yielding not-found
      httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`).flush([REPO_1]);
      harness.detectChanges();
      await harness.fixture.whenStable();
      harness.detectChanges();

      // Assert — not-found state renders with a back link; no form
      const el = harness.fixture.nativeElement as HTMLElement;
      const notFound = el.querySelector('.repository-page__not-found');
      expect(notFound).toBeTruthy();
      const form = el.querySelector('fd-repository-form');
      expect(form).toBeFalsy();
      const backLink = el.querySelector('.repository-page__back-link');
      expect(backLink).toBeTruthy();

      httpMock.verify();
    });
  });
});
