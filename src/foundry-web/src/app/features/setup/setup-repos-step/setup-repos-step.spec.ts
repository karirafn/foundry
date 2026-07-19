import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { provideRouter } from '@angular/router';
import { SetupReposStepComponent } from './setup-repos-step';
import { AvailableRepository } from '../../settings/repositories/repository.model';

const ACCOUNT_ID = 'account-1';

const AVAILABLE_REPOS: AvailableRepository[] = [
  { slug: 'org/repo-alpha', isPrivate: false, canPush: true },
  { slug: 'org/repo-beta', isPrivate: true, canPush: true },
  { slug: 'org/repo-gamma', isPrivate: false, canPush: false },
];

const AVAILABLE_REPOS_WITH_NON_WRITABLE: AvailableRepository[] = [
  { slug: 'org/repo-alpha', isPrivate: false, canPush: true },
  { slug: 'org/repo-readonly', isPrivate: false, canPush: false },
];

@Component({ template: '', standalone: true })
class StubIssuesComponent {}

function setup(accountId = ACCOUNT_ID) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SetupReposStepComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: 'issues', component: StubIssuesComponent }]),
    ],
  });

  const fixture = TestBed.createComponent(SetupReposStepComponent);
  fixture.componentRef.setInput('accountId', accountId);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, component: fixture.componentInstance, httpMock };
}

describe('SetupReposStepComponent', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // Cycle 1: renders search input, repo list, and action buttons
  it('should render a filter input, repo checkboxes, Back, Skip, and Finish buttons after loading', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('input[type="text"]')).toBeTruthy();
    expect(el.querySelectorAll('input[type="checkbox"]').length).toBe(3);
    expect(el.querySelector('button.setup-repos-step__back-btn')).toBeTruthy();
    expect(el.querySelector('button.setup-repos-step__skip-btn')).toBeTruthy();
    expect(el.querySelector('button.setup-repos-step__finish-btn')).toBeTruthy();
  });

  // Cycle 2: shows loading indicator while fetching repositories
  it('should show a loading indicator while repositories are being fetched', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.setup-repos-step__loading')).toBeTruthy();

    // Cleanup
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush([]);
  });

  // Cycle 3: Finish button is disabled when no repos are selected
  it('should disable the Finish button when no repositories are selected', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    expect(finishBtn.disabled).toBe(true);
  });

  // Cycle 4: selecting a checkbox enables the Finish button
  it('should enable the Finish button when at least one repository is selected', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelectorAll('input[type="checkbox"]')[0] as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Assert
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    expect(finishBtn.disabled).toBe(false);
  });

  // Cycle 5: filter narrows the displayed repository list
  it('should filter the repository list based on the text filter input', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const filterInput = el.querySelector('input[type="text"]') as HTMLInputElement;
    filterInput.value = 'alpha';
    filterInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    expect(el.querySelectorAll('input[type="checkbox"]').length).toBe(1);
    const label = el.querySelector('.setup-repos-step__repo-slug');
    expect(label?.textContent).toContain('alpha');
  });

  // Cycle 6: clicking Finish calls createRepository for each selected repo sequentially
  it('should call createRepository for each selected repository when Finish is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const checkboxes = el.querySelectorAll('input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    checkboxes[0].checked = true;
    checkboxes[0].dispatchEvent(new Event('change'));
    checkboxes[1].checked = true;
    checkboxes[1].dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    finishBtn.click();
    fixture.detectChanges();

    // Assert — repositories are created sequentially, one at a time
    const req1 = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`);
    expect(req1.request.method).toBe('POST');
    req1.flush({ id: 'r1', slug: req1.request.body.slug, accountId: ACCOUNT_ID, accountName: 'acc', pollIntervalSeconds: null, isActive: true, lastPolledAt: null });
    fixture.detectChanges();

    const req2 = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`);
    expect(req2.request.method).toBe('POST');
    req2.flush({ id: 'r2', slug: req2.request.body.slug, accountId: ACCOUNT_ID, accountName: 'acc', pollIntervalSeconds: null, isActive: true, lastPolledAt: null });
    fixture.detectChanges();

    // Both slugs should have been sent
    const slugs = [req1.request.body.slug, req2.request.body.slug].sort();
    expect(slugs).toEqual(['org/repo-alpha', 'org/repo-beta'].sort());
  });

  // Cycle 7: Finish button is disabled while saving
  it('should disable the Finish button while creation is in progress', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelectorAll('input[type="checkbox"]')[0] as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    finishBtn.click();
    fixture.detectChanges();

    // Assert
    expect(finishBtn.disabled).toBe(true);

    // Cleanup
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`);
    req.flush({ id: 'r1', slug: 'org/repo-alpha', accountId: ACCOUNT_ID, accountName: 'acc', pollIntervalSeconds: null, isActive: true, lastPolledAt: null });
  });

  // Cycle 8: Back button emits back output
  it('should emit the back event when the Back button is clicked', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    let emitted = false;
    component.back.subscribe(() => (emitted = true));

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('button.setup-repos-step__back-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 9: Skip button emits complete output without creating repos
  it('should emit the complete event and not call createRepository when Skip is clicked', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('button.setup-repos-step__skip-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(true);
    httpMock.expectNone(`/api/accounts/${ACCOUNT_ID}/repositories`);
  });

  // Cycle 9b: Finish button emits complete output after successful repository creation
  it('should emit the complete event after all repositories are created successfully', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelectorAll('input[type="checkbox"]')[0] as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    finishBtn.click();
    fixture.detectChanges();

    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush({
      id: 'r1',
      slug: 'org/repo-alpha',
      accountId: ACCOUNT_ID,
      accountName: 'acc',
      pollIntervalSeconds: null,
      isActive: true,
      lastPolledAt: null,
    });
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 10: shows error message when load fails
  it('should display an error message when loading repositories fails', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.setup-repos-step__load-error')).toBeTruthy();
    expect(el.querySelector('.setup-repos-step__load-error')?.textContent?.trim()).toBeTruthy();
  });

  // Cycle 11: shows progress indicator during creation
  it('should show a progress indicator while repositories are being created', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelectorAll('input[type="checkbox"]')[0] as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    finishBtn.click();
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.setup-repos-step__saving-indicator')).toBeTruthy();

    // Cleanup
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`);
    req.flush({ id: 'r1', slug: 'org/repo-alpha', accountId: ACCOUNT_ID, accountName: 'acc', pollIntervalSeconds: null, isActive: true, lastPolledAt: null });
  });

  // Cycle 12: shows save error message when creation fails
  it('should show an error message when creating repositories fails', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelectorAll('input[type="checkbox"]')[0] as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    finishBtn.click();
    fixture.detectChanges();

    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Repo already exists', {
      status: 409,
      statusText: 'Conflict',
    });
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.setup-repos-step__save-error')?.textContent?.trim()).toBeTruthy();
  });

  // Cycle 13: error message includes partial success count when some repos fail
  it('should include a count of successful repositories in the error message on partial failure', () => {
    // Arrange — select both writable repos; repo-gamma (index 2) is disabled so its change is ignored
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const checkboxes = el.querySelectorAll('input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    checkboxes[0].checked = true;
    checkboxes[0].dispatchEvent(new Event('change'));
    checkboxes[1].checked = true;
    checkboxes[1].dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    finishBtn.click();
    fixture.detectChanges();

    // Repositories are created sequentially — first succeeds, second fails
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush({
      id: 'r1', slug: 'org/repo-alpha', accountId: ACCOUNT_ID, accountName: 'acc', pollIntervalSeconds: null, isActive: true, lastPolledAt: null,
    });
    fixture.detectChanges();

    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const errorText = el.querySelector('.setup-repos-step__save-error')?.textContent?.trim() ?? '';
    expect(errorText).toContain('1 of 2');
  });

  // Cycle 15: non-writable repos render disabled with reason
  it('should render a disabled checkbox for non-writable repositories', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(AVAILABLE_REPOS_WITH_NON_WRITABLE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const checkboxes = el.querySelectorAll('input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    expect(checkboxes[0].disabled).toBe(false);
    expect(checkboxes[1].disabled).toBe(true);
  });

  it('should render the no-write-access reason for non-writable repositories', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(AVAILABLE_REPOS_WITH_NON_WRITABLE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const items = el.querySelectorAll('.setup-repos-step__repo-item') as NodeListOf<HTMLElement>;
    const readonlyItem = items[1];
    const reason = readonlyItem.querySelector('.setup-repos-step__repo-reason');
    expect(reason).toBeTruthy();
    expect(reason?.textContent).toContain('no write access');
  });

  it('should render a disabled modifier class on non-writable repo items', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(AVAILABLE_REPOS_WITH_NON_WRITABLE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const items = el.querySelectorAll('.setup-repos-step__repo-item') as NodeListOf<HTMLElement>;
    expect(items[0].classList.contains('setup-repos-step__repo-item--disabled')).toBe(false);
    expect(items[1].classList.contains('setup-repos-step__repo-item--disabled')).toBe(true);
  });

  it('should not add a non-writable slug to the selection when a programmatic change event fires on its checkbox', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(AVAILABLE_REPOS_WITH_NON_WRITABLE);
    fixture.detectChanges();

    // Act — dispatch a programmatic change event on the disabled (non-writable) checkbox
    const el = fixture.nativeElement as HTMLElement;
    const checkboxes = el.querySelectorAll('input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    const readonlyCheckbox = checkboxes[1];
    readonlyCheckbox.checked = true;
    readonlyCheckbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Assert — Finish button stays disabled because nothing was added to the selection
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    expect(finishBtn.disabled).toBe(true);
  });

  // Fix C: screen-reader double-announcement — visible reason span must be aria-hidden, sr-only span carries the id
  it('should render the visible reason span with aria-hidden="true" for non-writable repos', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(AVAILABLE_REPOS_WITH_NON_WRITABLE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const items = el.querySelectorAll('.setup-repos-step__repo-item') as NodeListOf<HTMLElement>;
    const readonlyItem = items[1];
    const visibleReason = readonlyItem.querySelector('.setup-repos-step__repo-reason');
    expect(visibleReason?.getAttribute('aria-hidden')).toBe('true');
  });

  it('should render a sr-only sibling with the reason text carrying the id referenced by aria-describedby', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(AVAILABLE_REPOS_WITH_NON_WRITABLE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const checkboxes = el.querySelectorAll('input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    const disabledCheckbox = checkboxes[1];
    const describedById = disabledCheckbox.getAttribute('aria-describedby');
    expect(describedById).toBeTruthy();
    const srOnlySpan = el.querySelector(`#${describedById}`);
    expect(srOnlySpan).toBeTruthy();
    expect(srOnlySpan?.classList.contains('sr-only')).toBe(true);
    expect(srOnlySpan?.textContent).toContain('no write access');
  });

  // Bug fix: replaceAll — nested-group slug (two slashes) must produce a slash-free id
  it('should produce matching, slash-free aria-describedby and id for a non-writable repo with a nested-group slug', () => {
    // Arrange
    const nestedGroupRepos: AvailableRepository[] = [
      { slug: 'group/subgroup/project', isPrivate: false, canPush: false },
    ];
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(nestedGroupRepos);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelector('input[type="checkbox"]') as HTMLInputElement;
    const describedById = checkbox.getAttribute('aria-describedby') ?? '';
    expect(describedById).toBeTruthy();
    expect(describedById).not.toContain('/');
    const srOnlySpan = el.querySelector(`#${describedById}`);
    expect(srOnlySpan).toBeTruthy();
    expect(srOnlySpan?.id).not.toContain('/');
    expect(srOnlySpan?.id).toBe(describedById);
  });

  it('should render all non-writable repos (not empty state) when all repos lack push access', () => {
    // Arrange
    const allReadonly: AvailableRepository[] = [
      { slug: 'org/readonly-a', isPrivate: false, canPush: false },
      { slug: 'org/readonly-b', isPrivate: false, canPush: false },
    ];
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(allReadonly);
    fixture.detectChanges();

    // Assert — both entries visible, no empty state
    const el = fixture.nativeElement as HTMLElement;
    const checkboxes = el.querySelectorAll('input[type="checkbox"]');
    expect(checkboxes.length).toBe(2);
    const emptyEl = el.querySelector('.setup-repos-step__repo-empty');
    expect(emptyEl).toBeNull();
  });

  // Cycle 16: error strings are truncated to 200 characters
  it('should truncate long server error strings to at most 200 characters in the error detail', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelectorAll('input[type="checkbox"]')[0] as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const longError = 'A'.repeat(300);

    // Act
    const finishBtn = el.querySelector('button.setup-repos-step__finish-btn') as HTMLButtonElement;
    finishBtn.click();
    fixture.detectChanges();

    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush(longError, {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — the raw error portion appended is at most 200 characters (full string may be longer due to the prefix)
    const errorText = el.querySelector('.setup-repos-step__save-error')?.textContent?.trim() ?? '';
    expect(errorText).toBeTruthy();
    // The error should not contain more than 200 A's (the raw error is truncated)
    expect((errorText.match(/A+/)?.[0] ?? '').length).toBeLessThanOrEqual(200);
  });
});
