import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { RepositoryPageComponent } from './repository-page';
import { RepositoryService } from '../repository.service';
import { AccountService } from '../../accounts/account.service';
import { RepositorySummary } from '../repository.model';
import { AccountSummary } from '../../accounts/account.model';

const ACCOUNT_1: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
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

  return { fixture, httpMock, repositoryService };
}

function seedRepositories(repositoryService: RepositoryService, repos: RepositorySummary[]): void {
  // Directly set signal state for unit tests — bypasses HTTP
  (repositoryService as unknown as { _repositoriesSignal: { set: (v: RepositorySummary[]) => void } })
    ._repositoriesSignal.set(repos);
  (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
    ._loadingSignal.set(false);
}

describe('RepositoryPageComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
  });

  describe('loaded state', () => {
    it('should render fd-repository-form when repository is found in service cache', () => {
      // Arrange
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      seedRepositories(repositoryService, [REPO_1]);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const form = el.querySelector('fd-repository-form');
      expect(form).toBeTruthy();
    });

    it('should render fd-repository-eligibility-details when eligibility is ineligible', () => {
      // Arrange
      const { fixture, repositoryService } = setup(REPO_1_INELIGIBLE.id);
      fixture.detectChanges();
      seedRepositories(repositoryService, [REPO_1_INELIGIBLE]);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const details = el.querySelector('fd-repository-eligibility-details');
      expect(details).toBeTruthy();
    });

    it('should not render fd-repository-eligibility-details when eligibility is eligible', () => {
      // Arrange
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      seedRepositories(repositoryService, [REPO_1]);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const details = el.querySelector('fd-repository-eligibility-details');
      expect(details).toBeFalsy();
    });

    it('should display the repository slug in the page heading', () => {
      // Arrange
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      seedRepositories(repositoryService, [REPO_1]);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const heading = el.querySelector('.repository-page__heading');
      expect(heading?.textContent?.trim()).toBe(REPO_1.slug);
    });
  });

  describe('not-found state', () => {
    it('should render not-found message when load settled and id absent', () => {
      // Arrange
      const { fixture, repositoryService } = setup('00000000-0000-0000-0000-000000000999');
      fixture.detectChanges();
      seedRepositories(repositoryService, [REPO_1]);

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
      const { fixture, repositoryService } = setup('00000000-0000-0000-0000-000000000999');
      fixture.detectChanges();
      seedRepositories(repositoryService, [REPO_1]);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const backLink = el.querySelector('.repository-page__back-link');
      expect(backLink).toBeTruthy();
    });
  });

  describe('load-error state', () => {
    function seedLoadError(repositoryService: RepositoryService, message: string): void {
      (repositoryService as unknown as { _loadErrorSignal: { set: (v: string | null) => void } })
        ._loadErrorSignal.set(message);
      (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
        ._loadingSignal.set(false);
    }

    it('should render load-error block when loadError signal is set', () => {
      // Arrange
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      seedLoadError(repositoryService, 'Network error');

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
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      seedLoadError(repositoryService, 'Network error');

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const errorBlock = el.querySelector('.repository-page__load-error');
      expect(errorBlock?.getAttribute('role')).toBe('alert');
    });

    it('should render the error message in the load-error block', () => {
      // Arrange
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      seedLoadError(repositoryService, "Couldn't load repositories. Check your connection and try again.");

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const message = el.querySelector('.repository-page__load-error-message');
      expect(message?.textContent?.trim()).toBe("Couldn't load repositories. Check your connection and try again.");
    });

    it('should render a Retry button in the load-error block', () => {
      // Arrange
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      seedLoadError(repositoryService, 'Network error');

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
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      seedLoadError(repositoryService, 'Network error');

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const backLink = el.querySelector('.repository-page__load-error .repository-page__back-link');
      expect(backLink).toBeTruthy();
      expect(backLink?.textContent?.trim()).toBe('Back to repositories');
    });

    it('should announce "Could not load repositories" in the status live region on load-error', () => {
      // Arrange
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      seedLoadError(repositoryService, 'Network error');

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
      const { fixture, repositoryService } = setup('00000000-0000-0000-0000-000000000999');
      fixture.detectChanges();
      seedRepositories(repositoryService, [REPO_1]);

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
      const { fixture, repositoryService } = setup(REPO_1.id);
      fixture.detectChanges();
      (repositoryService as unknown as { _loadErrorSignal: { set: (v: string | null) => void } })
        ._loadErrorSignal.set('Network error');
      (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
        ._loadingSignal.set(false);

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
      const { fixture, repositoryService } = setup(REPO_1.id);
      (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
        ._loadingSignal.set(true);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const loading = el.querySelector('.repository-page__loading');
      expect(loading).toBeTruthy();
      const form = el.querySelector('fd-repository-form');
      expect(form).toBeFalsy();
    });

    it('should not show not-found while loading (guards against premature not-found flash)', () => {
      // Arrange
      const { fixture, repositoryService } = setup('00000000-0000-0000-0000-000000000999');
      (repositoryService as unknown as { _loadingSignal: { set: (v: boolean) => void } })
        ._loadingSignal.set(true);

      // Act
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const notFound = el.querySelector('.repository-page__not-found');
      expect(notFound).toBeFalsy();
    });
  });
});
