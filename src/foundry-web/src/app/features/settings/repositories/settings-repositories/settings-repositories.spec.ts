import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SettingsRepositoriesComponent } from './settings-repositories';
import { AccountService } from '../../accounts/account.service';
import { RepositoryService } from '../repository.service';
import { AccountSummary } from '../../accounts/account.model';
import { CreateRepositoryResponse, RepositorySummary } from '../repository.model';

const ACCOUNT_1: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  tokenStatus: 'present',
  deniedRepositories: [],
  tokenInvalidSince: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const ACCOUNT_2: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  name: 'My GitLab',
  providerType: 'GitLab',
  baseUrl: 'https://gitlab.com',
  hasToken: true,
  tokenStatus: 'present',
  deniedRepositories: [],
  tokenInvalidSince: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
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
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  lastPolledAt: '2026-06-15T10:00:00Z',
  eligibility: { status: 'eligible', violations: [], reason: null },
};

const CREATED_1: CreateRepositoryResponse = {
  repository: REPO_1,
  branchRules: { kind: 'already-present', reason: null, settingsUrl: 'https://github.com/my-org/my-repo/settings/rules', detail: null },
};

const REPO_2: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000011',
  slug: 'my-org/another-repo',
  accountId: ACCOUNT_2.id,
  accountName: ACCOUNT_2.name,
  providerType: 'gitlab',
  position: 1,
  pollIntervalSeconds: null,
  effectivePollIntervalSeconds: 1800,
  pollIntervalIsDefault: true,
  isActive: false,
  maxConcurrentWorkers: 1,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  lastPolledAt: null,
  eligibility: { status: 'ineligible', violations: [{ rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' }], reason: null },
};

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsRepositoriesComponent],
    providers: [
      AccountService,
      RepositoryService,
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
    ],
  });

  const fixture = TestBed.createComponent(SettingsRepositoriesComponent);
  const httpMock = TestBed.inject(HttpTestingController);

  return { fixture, httpMock };
}

function flushAccounts(httpMock: HttpTestingController, accounts: AccountSummary[] = []): void {
  httpMock.expectOne('/api/accounts').flush(accounts);
}

function flushRepositories(httpMock: HttpTestingController, accountId: string, repos: RepositorySummary[] = []): void {
  httpMock.expectOne(`/api/accounts/${accountId}/repositories`).flush(repos);
}

describe('SettingsRepositoriesComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
  });

  it('should call loadAccounts on initialization', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/accounts');
    req.flush([]);
  });

  it('should render the Repositories section title', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const title = el.querySelector('.repositories-settings__section-title');
    expect(title?.textContent?.trim()).toBe('Repositories');
  });

  it('should render fd-repository-list in list view', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const list = el.querySelector('fd-repository-list');
    expect(list).toBeTruthy();
  });

  it('should load repositories for each account after accounts load', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    // Act
    flushAccounts(httpMock, [ACCOUNT_1, ACCOUNT_2]);
    fixture.detectChanges();

    // Assert — repositories requested for both accounts
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
    flushRepositories(httpMock, ACCOUNT_2.id, [REPO_2]);
  });

  it('should render fd-repository-form without repository when Add is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onAdd();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const form = el.querySelector('fd-repository-form');
    expect(form).toBeTruthy();
    const list = el.querySelector('fd-repository-list');
    expect(list).toBeFalsy();
  });

  it('should remain in list view after repositories load (edit no longer swaps the view)', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);

    // Act
    fixture.detectChanges();

    // Assert — the list is shown; no form (edit arm is gone; navigation is now handled by the list)
    const el = fixture.nativeElement as HTMLElement;
    const list = el.querySelector('fd-repository-list');
    expect(list).toBeTruthy();
    const form = el.querySelector('fd-repository-form');
    expect(form).toBeFalsy();
  });

  it('should return to list view after cancel', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();
    fixture.componentInstance.onAdd();
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onCancel();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const list = el.querySelector('fd-repository-list');
    expect(list).toBeTruthy();
    const form = el.querySelector('fd-repository-form');
    expect(form).toBeFalsy();
  });

  it('should call createRepository when saving from add view', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id);
    fixture.componentInstance.onAdd();
    fixture.detectChanges();
    fixture.componentInstance.onAccountSelected(ACCOUNT_1.id);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories/available-repositories`).flush({ hasClaims: false, repositories: [] });

    // Act
    fixture.componentInstance.onSave({ slug: 'my-org/new-repo', pollIntervalSeconds: 300, maxConcurrentWorkers: null });

    // Assert
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ slug: 'my-org/new-repo', pollIntervalSeconds: 300, maxConcurrentWorkers: null });
    req.flush(CREATED_1, { status: 201, statusText: 'Created' });

    // Flush reload
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
  });

  it('should show the add outcome, reload, and return to the list view on Done after a successful save', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
    fixture.componentInstance.onAdd();
    fixture.detectChanges();
    fixture.componentInstance.onAccountSelected(ACCOUNT_1.id);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories/available-repositories`).flush({ hasClaims: false, repositories: [] });

    // Act
    fixture.componentInstance.onSave({ slug: 'my-org/new-repo', pollIntervalSeconds: 300, maxConcurrentWorkers: null });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`).flush(CREATED_1, { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
    fixture.detectChanges();

    // Assert — the form stays and presents the outcome checklist
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('fd-repository-form fd-repository-add-outcome')).toBeTruthy();
    expect(el.querySelector('fd-repository-list')).toBeFalsy();

    // Act — Done returns to the list
    (el.querySelector('.repository-form__done-btn') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert — back in list view
    expect(el.querySelector('fd-repository-list')).toBeTruthy();
    expect(el.querySelector('fd-repository-form')).toBeFalsy();
  });

  it('should open fd-confirm-dialog and call deleteRepository when the dialog is confirmed', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
    fixture.detectChanges();
    const nativeConfirm = vi.fn();
    vi.stubGlobal('confirm', nativeConfirm);

    // Act
    fixture.componentInstance.onDelete(REPO_1);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const dialog = el.querySelector('fd-confirm-dialog dialog') as HTMLDialogElement;

    // Assert — the shared dialog asks, not the native confirm
    expect(dialog.open).toBe(true);
    expect(dialog.querySelector('.confirm-dialog__message')?.textContent).toContain(`Delete repository "${REPO_1.slug}"?`);
    expect(nativeConfirm).not.toHaveBeenCalled();
    httpMock.expectNone(`/api/accounts/${ACCOUNT_1.id}/repositories/${REPO_1.id}`);
    vi.unstubAllGlobals();

    // Act — confirm
    (dialog.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(dialog.open).toBe(false);
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories/${REPO_1.id}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    // Flush reload
    flushRepositories(httpMock, ACCOUNT_1.id, []);
  });

  it('should not call deleteRepository when the confirm dialog is dismissed', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onDelete(REPO_1);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const dialog = el.querySelector('fd-confirm-dialog dialog') as HTMLDialogElement;
    (dialog.querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert — dialog closed and no DELETE request
    expect(dialog.open).toBe(false);
    httpMock.expectNone(`/api/accounts/${ACCOUNT_1.id}/repositories/${REPO_1.id}`);
  });

  it('should call loadAvailableRepositories when account is selected', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id);
    fixture.componentInstance.onAdd();
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onAccountSelected(ACCOUNT_1.id);

    // Assert
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories/available-repositories`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('should render delete error container in list view', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const deleteError = el.querySelector('.repositories-settings__delete-error');
    expect(deleteError).toBeTruthy();
    expect(deleteError?.getAttribute('role')).toBe('alert');
  });

  it('should display delete error when deleteRepository fails', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
    fixture.detectChanges();

    // Act
    const repositoryService = TestBed.inject(RepositoryService);
    repositoryService.deleteRepository(ACCOUNT_1.id, REPO_1.id).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories/${REPO_1.id}`).flush(
      { type: 'tag:foundry,2026:problems/Repository.InUse', title: 'Conflict', status: 409, detail: 'Repository is in use.' },
      { status: 409, statusText: 'Conflict' }
    );
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const deleteError = el.querySelector('.repositories-settings__delete-error');
    expect(deleteError?.textContent).toContain('Repository is in use.');
  });
  describe('header and reorder mode', () => {
    function loadedWith(repos: RepositorySummary[]) {
      const ctx = setup();
      ctx.fixture.detectChanges();
      flushAccounts(ctx.httpMock, [ACCOUNT_1]);
      ctx.fixture.detectChanges();
      flushRepositories(ctx.httpMock, ACCOUNT_1.id, repos);
      ctx.fixture.detectChanges();
      return { ...ctx, el: ctx.fixture.nativeElement as HTMLElement };
    }

    function headerButtons(el: HTMLElement): string[] {
      return Array.from(el.querySelectorAll('.repositories-settings__actions button')).map(b => b.textContent?.trim() ?? '');
    }

    function headerButton(el: HTMLElement, label: string): HTMLButtonElement {
      const button = Array.from(el.querySelectorAll<HTMLButtonElement>('.repositories-settings__actions button'))
        .find(b => b.textContent?.trim() === label);
      if (!button) {
        throw new Error(`No header button ${label}`);
      }
      return button;
    }

    it('describes the priority order', () => {
      // Act
      const { el } = loadedWith([REPO_1]);

      // Assert
      expect(el.querySelector('.repositories-settings__section-description')?.textContent?.trim())
        .toBe('Foundry claims issues from the top repository first.');
    });

    it.each([
      [0, [] as RepositorySummary[]],
      [1, [REPO_1]],
    ])('shows only Add repository with %i repositories', (_count, repos) => {
      // Act
      const { el } = loadedWith(repos);

      // Assert
      expect(headerButtons(el)).toEqual(['Add repository']);
    });

    it('shows Reorder and Add repository with 2 repositories', () => {
      // Act
      const { el } = loadedWith([REPO_1, REPO_2]);

      // Assert
      expect(headerButtons(el)).toEqual(['Reorder', 'Add repository']);
    });

    it('opens the add form from the header Add repository button', () => {
      // Arrange
      const { el, fixture } = loadedWith([REPO_1]);

      // Act
      headerButton(el, 'Add repository').click();
      fixture.detectChanges();

      // Assert
      expect(el.querySelector('fd-repository-form')).toBeTruthy();
      expect(el.querySelector('fd-repository-list')).toBeNull();
      expect(el.querySelector('.repositories-settings__actions')).toBeNull();
    });

    it('enters reorder mode on Reorder, showing only Done and focusing it', async () => {
      // Arrange
      const { el, fixture } = loadedWith([REPO_1, REPO_2]);

      // Act
      headerButton(el, 'Reorder').click();
      fixture.detectChanges();
      await fixture.whenStable();

      // Assert
      expect(headerButtons(el)).toEqual(['Done']);
      expect(el.querySelectorAll('.repository-list__rank--button').length).toBe(2);
      expect(document.activeElement?.textContent?.trim()).toBe('Done');
    });

    it('leaves reorder mode on Done, restoring the buttons and focusing Reorder', async () => {
      // Arrange
      const { el, fixture } = loadedWith([REPO_1, REPO_2]);
      headerButton(el, 'Reorder').click();
      fixture.detectChanges();
      await fixture.whenStable();

      // Act
      headerButton(el, 'Done').click();
      fixture.detectChanges();
      await fixture.whenStable();

      // Assert
      expect(headerButtons(el)).toEqual(['Reorder', 'Add repository']);
      expect(el.querySelector('.repository-list__rank--button')).toBeNull();
      expect(document.activeElement?.textContent?.trim()).toBe('Reorder');
    });

    it('leaves reorder mode by itself when the list drops below two repositories', () => {
      // Arrange
      const { el, fixture, httpMock } = loadedWith([REPO_1, REPO_2]);
      headerButton(el, 'Reorder').click();
      fixture.detectChanges();

      // Act
      fixture.componentInstance.reloadRepositories();
      flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
      fixture.detectChanges();

      // Assert
      expect(headerButtons(el)).not.toContain('Done');
      expect(el.querySelector('.repository-list__rank--button')).toBeNull();
    });
  });
});
