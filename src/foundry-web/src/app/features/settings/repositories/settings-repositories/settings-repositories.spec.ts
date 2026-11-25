import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SettingsRepositoriesComponent } from './settings-repositories';
import { AccountService } from '../../accounts/account.service';
import { RepositoryService } from '../repository.service';
import { AccountSummary } from '../../accounts/account.model';
import { CreateRepositoryResponse, RepositorySummary, UnclaimedRepository } from '../repository.model';
import { provideFakeHubs } from '../../../../core/testing/fake-hub-transport';

const ACCOUNT_1: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  tokenStatus: 'present',
  deniedRepositories: [],
  tokenInvalidSince: null,
  nonConformingToken: null,
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
  nonConformingToken: null,
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
  reviewPullRequests: true,
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
  reviewPullRequests: true,
  lastPolledAt: null,
  eligibility: { status: 'ineligible', violations: [{ rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' }], reason: null },
};

const UNCLAIMED_URL = '/api/repositories/unclaimed';

const UNCLAIMED_1: UnclaimedRepository = {
  id: '00000000-0000-0000-0000-000000000020',
  host: 'gitlab.example.com',
  slug: 'orphan/project',
  violation: 'no-credential:orphan',
  violationDescription: 'No account covers the "orphan" namespace.',
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
      provideFakeHubs(),
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
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.match(UNCLAIMED_URL).forEach(req => req.flush([]));
    httpMock.verify({ ignoreCancelled: true });
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
    expect(dialog.querySelector('.confirm-dialog__message')?.textContent?.trim()).toBe(
      `Foundry will stop monitoring "${REPO_1.slug}", delete its webhooks, remove its open issues and discard any unpublished runs. Completed issues stay in History. Adding the repository again restores its history, and its open issues return on the next poll.`
    );
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
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
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
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 409, url: `/api/accounts/${ACCOUNT_1.id}/repositories/${REPO_1.id}` }));
  });

  it('should list the blocking issues when deleteRepository is refused', () => {
    // Arrange
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
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
      {
        type: 'tag:foundry,2026:problems/Repository.RemovalBlocked',
        title: 'Conflict',
        status: 409,
        detail: 'refused',
        blockingIssues: [{ repositorySlug: 'acme/api', issueNumber: 42, state: 'in_progress' }],
      },
      { status: 409, statusText: 'Conflict' }
    );
    fixture.detectChanges();

    // Assert
    const alert = (fixture.nativeElement as HTMLElement).querySelector('.repositories-settings__delete-error');
    expect(alert?.querySelector('.delete-failure__lead')?.textContent?.trim())
      .toBe('Repository not deleted — a worker is still active on:');
    expect(alert?.querySelector('.delete-failure__issues li')?.textContent?.trim()).toBe('acme/api#42 — in progress');
    expect(alert?.querySelector('.delete-failure__hint')?.textContent?.trim()).toBe('Try again once these finish.');
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 409, url: `/api/accounts/${ACCOUNT_1.id}/repositories/${REPO_1.id}` }));
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

  describe('unclaimed repositories', () => {
    function unclaimedSection(el: HTMLElement): HTMLElement | null {
      return el.querySelector('section[aria-labelledby="unclaimed-repositories-heading"]');
    }

    function render(unclaimed: UnclaimedRepository[], accounts: AccountSummary[] = [ACCOUNT_1]) {
      const { fixture, httpMock } = setup();
      fixture.detectChanges();
      httpMock.expectOne(UNCLAIMED_URL).flush(unclaimed);
      flushAccounts(httpMock, accounts);
      fixture.detectChanges();
      for (const account of accounts) {
        flushRepositories(httpMock, account.id, []);
      }
      fixture.detectChanges();
      return { fixture, httpMock, el: fixture.nativeElement as HTMLElement };
    }

    it('hides the section when no repository is unclaimed', () => {
      // Act
      const { el } = render([]);

      // Assert
      expect(unclaimedSection(el)).toBeNull();
    });

    it('shows the section above the Repositories card with its heading, description and rows', () => {
      // Act
      const { el } = render([UNCLAIMED_1]);

      // Assert
      const section = unclaimedSection(el);
      expect(section).not.toBeNull();
      const sections = Array.from(el.querySelectorAll('.repositories-settings__section'));
      expect(sections.indexOf(section!)).toBe(0);
      expect(section!.querySelector('#unclaimed-repositories-heading')?.textContent?.trim()).toBe('Unclaimed repositories');
      expect(section!.querySelector('.repositories-settings__section-description')?.textContent?.trim()).toBe(
        "No account's namespace claim covers these repositories, so Foundry can't poll, dispatch or publish them. Add the namespace to an account in Accounts, or delete the repository. Foundry re-checks claims every hour while any repository is unclaimed."
      );
      expect(section!.querySelector('a')?.getAttribute('href')).toBe('/settings/accounts');
      expect(section!.querySelector('.unclaimed-repository-list__slug')?.textContent?.trim()).toBe('orphan/project');
    });

    it('stays shown in the add view', () => {
      // Arrange
      const { fixture, el } = render([UNCLAIMED_1]);

      // Act
      fixture.componentInstance.onAdd();
      fixture.detectChanges();

      // Assert
      expect(unclaimedSection(el)).not.toBeNull();
    });

    it('shows the load error with Retry even when the list is empty', () => {
      // Arrange
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture, httpMock } = setup();
      fixture.detectChanges();
      httpMock.expectOne(UNCLAIMED_URL).flush({ detail: 'Database unavailable.' }, { status: 500, statusText: 'Server Error' });
      flushAccounts(httpMock);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      // Act
      el.querySelector<HTMLButtonElement>('.unclaimed-repository-list__retry-btn')?.click();

      // Assert
      expect(unclaimedSection(el)?.querySelector('.unclaimed-repository-list__error-message')?.textContent?.trim())
        .toBe('Database unavailable.');
      httpMock.expectOne(UNCLAIMED_URL).flush([]);
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 500, url: UNCLAIMED_URL }));
    });

    it('confirms with the repository dialog and deletes through /api/repositories/{id}, then reloads both lists', () => {
      // Arrange
      const { fixture, httpMock, el } = render([UNCLAIMED_1]);

      // Act
      el.querySelector<HTMLButtonElement>('.unclaimed-repository-list__delete-btn')?.click();
      fixture.detectChanges();
      const dialog = el.querySelector('fd-confirm-dialog dialog') as HTMLDialogElement;

      // Assert — the one dialog asks with the repository message
      expect(dialog.open).toBe(true);
      expect(dialog.querySelector('.confirm-dialog__message')?.textContent?.trim()).toBe(
        `Foundry will stop monitoring "${UNCLAIMED_1.slug}", delete its webhooks, remove its open issues and discard any unpublished runs. Completed issues stay in History. Adding the repository again restores its history, and its open issues return on the next poll.`
      );

      // Act — confirm
      (dialog.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
      fixture.detectChanges();

      // Assert
      const req = httpMock.expectOne(`/api/repositories/${UNCLAIMED_1.id}`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
      flushRepositories(httpMock, ACCOUNT_1.id, []);
      httpMock.expectOne(UNCLAIMED_URL).flush([]);
      fixture.detectChanges();
      expect(unclaimedSection(el)).toBeNull();
    });

    it('disables the row being removed', () => {
      // Arrange
      const { fixture, httpMock, el } = render([UNCLAIMED_1]);
      fixture.componentInstance.onDeleteUnclaimed(UNCLAIMED_1);

      // Act
      fixture.componentInstance.confirmDeleteRepository();
      fixture.detectChanges();

      // Assert
      expect(el.querySelector<HTMLButtonElement>('.unclaimed-repository-list__delete-btn')?.disabled).toBe(true);
      httpMock.expectOne(`/api/repositories/${UNCLAIMED_1.id}`).flush(null, { status: 204, statusText: 'No Content' });
      flushRepositories(httpMock, ACCOUNT_1.id, []);
    });

    it('lists the blocking issues in the section alert when the removal is refused', () => {
      // Arrange
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture, httpMock, el } = render([UNCLAIMED_1]);
      fixture.componentInstance.onDeleteUnclaimed(UNCLAIMED_1);

      // Act
      fixture.componentInstance.confirmDeleteRepository();
      httpMock.expectOne(`/api/repositories/${UNCLAIMED_1.id}`).flush(
        {
          title: 'Conflict',
          status: 409,
          detail: 'refused',
          blockingIssues: [{ repositorySlug: 'orphan/project', issueNumber: 4, state: 'in_progress' }],
        },
        { status: 409, statusText: 'Conflict' },
      );
      httpMock.expectOne(UNCLAIMED_URL).flush([UNCLAIMED_1]);
      fixture.detectChanges();

      // Assert
      const alert = unclaimedSection(el)?.querySelector('.repositories-settings__delete-error');
      expect(alert?.getAttribute('role')).toBe('alert');
      expect(alert?.querySelector('.delete-failure__lead')?.textContent?.trim())
        .toBe('Repository not deleted — a worker is still active on:');
      expect(alert?.querySelector('.delete-failure__issues li')?.textContent?.trim()).toBe('orphan/project#4 — in progress');
      expect(el.querySelector<HTMLButtonElement>('.unclaimed-repository-list__delete-btn')?.disabled).toBe(false);
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 409, url: `/api/repositories/${UNCLAIMED_1.id}` }));
    });

    it('focuses the Repositories heading after removing the last unclaimed repository', async () => {
      // Arrange
      const { fixture, httpMock, el } = render([UNCLAIMED_1]);
      fixture.componentInstance.onDeleteUnclaimed(UNCLAIMED_1);

      // Act
      fixture.componentInstance.confirmDeleteRepository();
      httpMock.expectOne(`/api/repositories/${UNCLAIMED_1.id}`).flush(null, { status: 204, statusText: 'No Content' });
      flushRepositories(httpMock, ACCOUNT_1.id, []);
      httpMock.expectOne(UNCLAIMED_URL).flush([]);
      await fixture.whenStable();
      fixture.detectChanges();
      await fixture.whenStable();

      // Assert
      expect(document.activeElement?.textContent?.trim()).toBe('Repositories');
    });

    describe('re-check claims', () => {
      const RECHECK_URL = '/api/repositories/unclaimed/recheck';
      const UNCLAIMED_2: UnclaimedRepository = { ...UNCLAIMED_1, id: '00000000-0000-0000-0000-000000000021', slug: 'orphan/other' };

      function liveRegion(el: HTMLElement): HTMLElement | null {
        return el.querySelector('.repositories-settings > [role="status"]');
      }

      function recheck(
        fixture: ReturnType<typeof render>['fixture'],
        httpMock: HttpTestingController,
        el: HTMLElement,
        reclaimedRepositoryCount: number,
        unclaimedAfter: UnclaimedRepository[],
      ): void {
        el.querySelector<HTMLButtonElement>('.unclaimed-repository-list__recheck-btn')?.click();
        const req = httpMock.expectOne(r => r.url === RECHECK_URL);
        expect(req.request.method).toBe('POST');
        expect(req.request.params.get('host')).toBe('gitlab.example.com');
        req.flush({ reclaimedRepositoryCount });
        flushRepositories(httpMock, ACCOUNT_1.id, []);
        httpMock.expectOne(UNCLAIMED_URL).flush(unclaimedAfter);
        fixture.detectChanges();
      }

      it('keeps a persistent polite live region at the page root', () => {
        // Act
        const { el } = render([]);

        // Assert
        const region = liveRegion(el);
        expect(region?.classList.contains('sr-only')).toBe(true);
        expect(region?.getAttribute('aria-live')).toBe('polite');
        expect(region?.getAttribute('aria-atomic')).toBe('true');
        expect(region?.textContent?.trim()).toBe('');
      });

      it('announces the reclaimed count and repeats it under the description while the section shows', () => {
        // Arrange
        const { fixture, httpMock, el } = render([UNCLAIMED_1, UNCLAIMED_2]);

        // Act
        recheck(fixture, httpMock, el, 1, [UNCLAIMED_2]);

        // Assert
        expect(liveRegion(el)?.textContent?.trim()).toBe('1 repository claimed again.');
        const result = unclaimedSection(el)?.querySelector('.repositories-settings__claims-result');
        expect(result?.textContent?.trim()).toBe('1 repository claimed again.');
        expect(result?.hasAttribute('role')).toBe(false);
        expect(result?.hasAttribute('aria-live')).toBe(false);
      });

      it('uses the plural for more than one reclaimed repository and still announces after the section hides', () => {
        // Arrange
        const { fixture, httpMock, el } = render([UNCLAIMED_1, UNCLAIMED_2]);

        // Act
        recheck(fixture, httpMock, el, 2, []);

        // Assert
        expect(unclaimedSection(el)).toBeNull();
        expect(liveRegion(el)?.textContent?.trim()).toBe('2 repositories claimed again.');
      });

      it('names the host when no account claims the repositories yet', () => {
        // Arrange
        const { fixture, httpMock, el } = render([UNCLAIMED_1]);

        // Act
        recheck(fixture, httpMock, el, 0, [UNCLAIMED_1]);

        // Assert
        expect(liveRegion(el)?.textContent?.trim()).toBe('No account claims these gitlab.example.com repositories yet.');
        expect(unclaimedSection(el)?.querySelector('.repositories-settings__claims-result')?.textContent?.trim())
          .toBe('No account claims these gitlab.example.com repositories yet.');
      });

      it('clears the result when the next re-check starts', () => {
        // Arrange
        const { fixture, httpMock, el } = render([UNCLAIMED_1]);
        recheck(fixture, httpMock, el, 0, [UNCLAIMED_1]);

        // Act
        el.querySelector<HTMLButtonElement>('.unclaimed-repository-list__recheck-btn')?.click();
        fixture.detectChanges();

        // Assert
        expect(liveRegion(el)?.textContent?.trim()).toBe('');
        expect(unclaimedSection(el)?.querySelector('.repositories-settings__claims-result')).toBeNull();
        httpMock.expectOne(r => r.url === RECHECK_URL).flush({ reclaimedRepositoryCount: 0 });
        flushRepositories(httpMock, ACCOUNT_1.id, []);
      });

      it('clears the result when a remove starts', () => {
        // Arrange
        const { fixture, httpMock, el } = render([UNCLAIMED_1]);
        recheck(fixture, httpMock, el, 0, [UNCLAIMED_1]);
        fixture.componentInstance.onDeleteUnclaimed(UNCLAIMED_1);

        // Act
        fixture.componentInstance.confirmDeleteRepository();
        fixture.detectChanges();

        // Assert
        expect(liveRegion(el)?.textContent?.trim()).toBe('');
        httpMock.expectOne(`/api/repositories/${UNCLAIMED_1.id}`).flush(null, { status: 204, statusText: 'No Content' });
        flushRepositories(httpMock, ACCOUNT_1.id, []);
      });

      it('clears the unclaimed delete failure when a re-check starts', () => {
        // Arrange
        const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
        const { fixture, httpMock, el } = render([UNCLAIMED_1]);
        fixture.componentInstance.onDeleteUnclaimed(UNCLAIMED_1);
        fixture.componentInstance.confirmDeleteRepository();
        httpMock.expectOne(`/api/repositories/${UNCLAIMED_1.id}`).flush(
          { title: 'Conflict', status: 409, detail: 'refused', blockingIssues: [{ repositorySlug: 'orphan/project', issueNumber: 4, state: 'in_progress' }] },
          { status: 409, statusText: 'Conflict' },
        );
        httpMock.expectOne(UNCLAIMED_URL).flush([UNCLAIMED_1]);
        fixture.detectChanges();

        // Act
        el.querySelector<HTMLButtonElement>('.unclaimed-repository-list__recheck-btn')?.click();
        fixture.detectChanges();

        // Assert
        expect(unclaimedSection(el)?.querySelector('.delete-failure__lead')).toBeNull();
        httpMock.expectOne(r => r.url === RECHECK_URL).flush({ reclaimedRepositoryCount: 0 });
        flushRepositories(httpMock, ACCOUNT_1.id, []);
        expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 409, url: `/api/repositories/${UNCLAIMED_1.id}` }));
      });

      it('focuses the Repositories heading when the re-check reclaims the last unclaimed repository', async () => {
        // Arrange
        const { fixture, httpMock, el } = render([UNCLAIMED_1]);
        el.querySelector<HTMLButtonElement>('.unclaimed-repository-list__recheck-btn')?.click();

        // Act
        httpMock.expectOne(r => r.url === RECHECK_URL).flush({ reclaimedRepositoryCount: 1 });
        flushRepositories(httpMock, ACCOUNT_1.id, []);
        httpMock.expectOne(UNCLAIMED_URL).flush([]);
        await fixture.whenStable();
        fixture.detectChanges();
        await fixture.whenStable();

        // Assert
        expect(document.activeElement?.textContent?.trim()).toBe('Repositories');
      });
    });
  });
});
