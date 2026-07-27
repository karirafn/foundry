import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsRepositoriesComponent } from './settings-repositories';
import { AccountService } from '../accounts/account.service';
import { RepositoryService } from './repository.service';
import { AccountSummary } from '../accounts/account.model';
import { RepositorySummary } from './repository.model';

const ACCOUNT_1: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  namespaces: [],
};

const ACCOUNT_2: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  name: 'My GitLab',
  providerType: 'GitLab',
  baseUrl: 'https://gitlab.com',
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
  lastPolledAt: '2026-06-15T10:00:00Z',
  eligibility: { status: 'eligible', violations: [] },
};

const REPO_2: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000011',
  slug: 'my-org/another-repo',
  accountId: ACCOUNT_2.id,
  accountName: ACCOUNT_2.name,
  providerType: 'gitlab',
  position: 1,
  pollIntervalSeconds: null,
  isActive: false,
  lastPolledAt: null,
  eligibility: { status: 'ineligible', violations: [{ rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' }] },
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

  it('should render fd-repository-form with repository data when Edit is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onEdit(REPO_1);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const form = el.querySelector('fd-repository-form');
    expect(form).toBeTruthy();
    const list = el.querySelector('fd-repository-list');
    expect(list).toBeFalsy();
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
    fixture.componentInstance.onSave({ slug: 'my-org/new-repo', pollIntervalSeconds: 300 });

    // Assert
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ slug: 'my-org/new-repo', pollIntervalSeconds: 300 });
    req.flush(REPO_1, { status: 201, statusText: 'Created' });

    // Flush reload
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
  });

  it('should call updateRepository when saving from edit view', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
    fixture.componentInstance.onEdit(REPO_1);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onSave({ pollIntervalSeconds: 600, isActive: false });

    // Assert
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories/${REPO_1.id}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ pollIntervalSeconds: 600, isActive: false });
    req.flush({ ...REPO_1, pollIntervalSeconds: 600, isActive: false });

    // Flush reload
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
  });

  it('should return to list view and reload after successful save', () => {
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
    fixture.componentInstance.onSave({ slug: 'my-org/new-repo', pollIntervalSeconds: 300 });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories`).flush(REPO_1, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    // Assert — back in list view
    const el = fixture.nativeElement as HTMLElement;
    const list = el.querySelector('fd-repository-list');
    expect(list).toBeTruthy();
    const form = el.querySelector('fd-repository-form');
    expect(form).toBeFalsy();

    // Flush the reload requests
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
  });

  it('should show confirm dialog and call deleteRepository when onDelete is confirmed', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    // Act
    fixture.componentInstance.onDelete(REPO_1);

    // Assert
    expect(window.confirm).toHaveBeenCalledWith(`Delete repository "${REPO_1.slug}"?`);
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories/${REPO_1.id}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    // Flush reload
    flushRepositories(httpMock, ACCOUNT_1.id, []);
  });

  it('should not call deleteRepository when confirmation is declined', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [ACCOUNT_1]);
    fixture.detectChanges();
    flushRepositories(httpMock, ACCOUNT_1.id, [REPO_1]);
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    // Act
    fixture.componentInstance.onDelete(REPO_1);

    // Assert — no DELETE request
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
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    // Act
    const repositoryService = TestBed.inject(RepositoryService);
    repositoryService.deleteRepository(ACCOUNT_1.id, REPO_1.id).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_1.id}/repositories/${REPO_1.id}`).flush('Repository is in use.', {
      status: 409,
      statusText: 'Conflict',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const deleteError = el.querySelector('.repositories-settings__delete-error');
    expect(deleteError?.textContent).toContain('Repository is in use.');
  });
});
