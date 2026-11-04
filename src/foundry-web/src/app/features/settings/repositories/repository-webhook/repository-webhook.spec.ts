import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RepositoryWebhookComponent } from './repository-webhook';
import { RepositoryWebhookInfo } from '../repository.model';

const ACCOUNT_ID = '00000000-0000-0000-0000-000000000001';
const REPO_ID = '00000000-0000-0000-0000-000000000010';
const BASE = `/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}/webhook`;

const NOT_REGISTERED: RepositoryWebhookInfo = {
  status: 'not-registered',
  payloadUrl: 'https://hooks.example.com/webhooks/github/abc',
  contentType: 'application/json',
  events: ['issues', 'pull_request', 'check_suite'],
  backstopIntervalMinutes: 15,
};

function setup(info: RepositoryWebhookInfo) {
  TestBed.configureTestingModule({
    imports: [RepositoryWebhookComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });

  const fixture = TestBed.createComponent(RepositoryWebhookComponent);
  fixture.componentRef.setInput('accountId', ACCOUNT_ID);
  fixture.componentRef.setInput('repositoryId', REPO_ID);
  const httpMock = TestBed.inject(HttpTestingController);

  fixture.detectChanges();
  httpMock.expectOne(BASE).flush(info);
  fixture.detectChanges();

  const el = fixture.nativeElement as HTMLElement;
  return { fixture, httpMock, el };
}

describe('RepositoryWebhookComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('shows the manual registration instructions without the secret when not registered', () => {
    // Arrange & Act
    const { el } = setup(NOT_REGISTERED);

    // Assert
    expect(el.querySelector('.repository-webhook__status-value')?.textContent?.trim()).toBe('Not registered');
    expect(el.textContent).toContain(NOT_REGISTERED.payloadUrl!);
    expect(el.textContent).toContain('application/json');
    const events = Array.from(el.querySelectorAll('.repository-webhook__events li')).map(li => li.textContent?.trim());
    expect(events).toEqual(NOT_REGISTERED.events);
    expect(el.querySelector('.repository-webhook__secret')).toBeNull();
  });

  it('reveals the secret only on demand', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(NOT_REGISTERED);
    const reveal = Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes('Reveal secret'))!;

    // Act
    reveal.click();
    const request = httpMock.expectOne(`${BASE}/secret`);
    request.flush({ secret: 's3cr3t' });
    fixture.detectChanges();

    // Assert
    expect(request.request.method).toBe('POST');
    expect(el.querySelector('.repository-webhook__secret')?.textContent?.trim()).toBe('s3cr3t');
  });

  it('records the registration and shows the waiting hint', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(NOT_REGISTERED);
    const added = Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes("I've added the webhook"))!;

    // Act
    added.click();
    httpMock.expectOne(`${BASE}/registered`).flush({ ...NOT_REGISTERED, status: 'pending' });
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.repository-webhook__status-value')?.textContent?.trim()).toBe('Waiting for first delivery');
    expect(el.textContent).toContain('redeliver the ping');
  });

  it('hides the instructions when the webhook is active', () => {
    // Arrange & Act
    const { el } = setup({ ...NOT_REGISTERED, status: 'active' });

    // Assert
    expect(el.querySelector('.repository-webhook__status-value')?.textContent?.trim()).toBe('Active');
    expect(el.querySelector('.repository-webhook__details')).toBeNull();
    expect(el.textContent).toContain('every 15 minutes');
  });

  it('explains how to configure webhooks when ingress is not configured', () => {
    // Arrange & Act
    const { el } = setup({ ...NOT_REGISTERED, status: 'not-configured', payloadUrl: null });

    // Assert
    expect(el.querySelector('.repository-webhook__status-value')?.textContent?.trim()).toBe('Not configured');
    expect(el.textContent).toContain('Webhooks:Port');
    expect(el.querySelector('.repository-webhook__details')).toBeNull();
  });
});
