import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { provideRouter } from '@angular/router';
import { SetupReposStepComponent } from './setup-repos-step';
import { AvailableRepository } from '../../settings/repositories/repository.model';

const ACCOUNT_ID = 'account-1';

const AVAILABLE_REPOS: AvailableRepository[] = [
  { slug: 'org/repo-alpha', isPrivate: false },
  { slug: 'org/repo-beta', isPrivate: true },
  { slug: 'org/repo-gamma', isPrivate: false },
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

  // Cycle 6: clicking Finish calls createRepository for each selected repo
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

    // Assert
    const reqs = httpMock.match(`/api/accounts/${ACCOUNT_ID}/repositories`);
    expect(reqs.length).toBe(2);
    const slugs = reqs.map(r => r.request.body.slug).sort();
    expect(slugs).toEqual(['org/repo-alpha', 'org/repo-beta'].sort());

    // Cleanup
    reqs.forEach(r => r.flush({ id: 'r1', slug: r.request.body.slug, accountId: ACCOUNT_ID, accountName: 'acc', pollIntervalSeconds: null, isActive: true, lastPolledAt: null }));
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

  // Cycle 9: Skip button navigates to /issues without creating repos
  it('should navigate to /issues and not call createRepository when Skip is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush(AVAILABLE_REPOS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('button.setup-repos-step__skip-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert — no create requests should be pending
    httpMock.expectNone(`/api/accounts/${ACCOUNT_ID}/repositories`);
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
});
