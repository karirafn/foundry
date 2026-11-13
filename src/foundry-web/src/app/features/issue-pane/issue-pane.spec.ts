import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import { WorkerSignalRService } from '../../core/services/worker-signalr.service';
import { IssueSummary } from '../issues/issue.model';
import { IssueService } from '../issues/issue.service';
import { IssuePaneComponent } from './issue-pane';

const summary: IssueSummary = {
  id: 'f',
  issueNumber: 142,
  title: 'Add retry to the webhook relay',
  state: 'in_progress',
  repositorySlug: 'acme/api',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/acme/api/issues/142',
  stage: 'implement',
  needsAction: false,
  awaitsDispatch: false,
  codeStep: null,
};

const detail = { ...summary, author: 'dev', labels: [], stateDetails: null };

@Component({
  imports: [IssuePaneComponent],
  template: `<fd-issue-pane [issueId]="issueId()" [summary]="summary()" (closed)="closes = closes + 1" />`,
})
class HostComponent {
  readonly issueId = signal('f');
  readonly summary = signal<IssueSummary | null>(summary);
  closes = 0;
}

async function setup(withSummary = true) {
  TestBed.configureTestingModule({
    imports: [HostComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: IssueSignalRService,
        useValue: { on: () => {}, off: () => {}, onReconnected: () => {}, connectionStatus: signal('connected') },
      },
      {
        provide: WorkerSignalRService,
        useValue: {
          activeAgentCount: signal(0),
          activityForIssue: () => null,
          agentActivityForIssue: () => null,
          activityFor: () => null,
          logStreamFor: () => null,
        },
      },
    ],
  });
  const service = TestBed.inject(IssueService);
  const httpMock = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(HostComponent);
  if (!withSummary) {
    fixture.componentInstance.summary.set(null);
  }
  document.body.appendChild(fixture.nativeElement);
  service.selectIssue('f');
  const render = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
  };
  await render();
  return { fixture, host: fixture.componentInstance, el: fixture.nativeElement as HTMLElement, service, httpMock, render };
}

describe('IssuePaneComponent', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('heads the pane with the issue number and title from the summary', async () => {
    // Act
    const { el, httpMock } = await setup();

    // Assert
    httpMock.expectOne('/api/issues/f');
    const heading = el.querySelector('h2#issue-pane-title')!;
    expect(heading.textContent?.trim()).toBe('#142 Add retry to the webhook relay');
    expect(heading.getAttribute('title')).toBe('Add retry to the webhook relay');
    expect(heading.getAttribute('tabindex')).toBe('-1');
  });

  it('hosts the issue detail', async () => {
    // Arrange
    const { el, httpMock, render } = await setup();

    // Act
    httpMock.expectOne('/api/issues/f').flush(detail);
    await render();

    // Assert
    expect(el.querySelector('fd-issue-detail')).not.toBeNull();
  });

  it('shows a busy heading without a summary until the detail lands', async () => {
    // Act
    const { el, httpMock } = await setup(false);

    // Assert
    httpMock.expectOne('/api/issues/f');
    const heading = el.querySelector('h2#issue-pane-title')!;
    expect(heading.getAttribute('aria-busy')).toBe('true');
    expect(heading.textContent?.trim()).toBe('Loading issue…');
  });

  it('heads the pane from the detail when there is no summary', async () => {
    // Arrange
    const { el, httpMock, render } = await setup(false);

    // Act
    httpMock.expectOne('/api/issues/f').flush(detail);
    await render();

    // Assert
    const heading = el.querySelector('h2#issue-pane-title')!;
    expect(heading.getAttribute('aria-busy')).toBeNull();
    expect(heading.textContent?.trim()).toBe('#142 Add retry to the webhook relay');
  });

  it('says the issue is no longer tracked when its detail is gone', async () => {
    // Arrange
    const { el, httpMock, render } = await setup();

    // Act
    httpMock.expectOne('/api/issues/f').flush('Not Found', { status: 404, statusText: 'Not Found' });
    await render();

    // Assert
    expect(el.textContent).toContain('This issue is no longer tracked.');
    expect(el.querySelector('fd-issue-detail')).toBeNull();
  });

  it('has a named close button that asks to close the pane', async () => {
    // Arrange
    const { el, host, httpMock } = await setup();
    httpMock.expectOne('/api/issues/f');

    // Act
    (el.querySelector('button[aria-label="Close issue details"]') as HTMLButtonElement).click();

    // Assert
    expect(host.closes).toBe(1);
  });

  it('moves focus to the heading when it swaps to another issue', async () => {
    // Arrange
    const { host, httpMock, render, service } = await setup();
    httpMock.expectOne('/api/issues/f');

    // Act
    service.selectIssue('g');
    host.summary.set({ ...summary, id: 'g', issueNumber: 7, title: 'Second' });
    host.issueId.set('g');
    await render();

    // Assert
    httpMock.expectOne('/api/issues/g');
    expect(document.activeElement?.id).toBe('issue-pane-title');
    expect(document.activeElement?.textContent?.trim()).toBe('#7 Second');
  });
});
