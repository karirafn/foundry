import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
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
  waitsOnBlockers: false,
  codeStep: null,
  repositoryId: 'repo-1',
  enteredStateAt: '2026-01-01T00:00:00Z',
};

// The detail as the API sends it: it carries none of the summary's placement or provider fields.
const detail = {
  id: 'f',
  issueNumber: 142,
  title: 'Add retry to the webhook relay',
  state: 'in_progress',
  repositorySlug: 'acme/api',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/acme/api/issues/142',
  author: 'dev',
  labels: [],
  stateDetails: null,
  kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
  triage: null,
};

const runningRun = {
  workerRunId: 'run-1',
  issueId: 'f',
  role: 'implement',
  state: 'running',
  failureCategory: null,
  failureSummary: null,
  resultText: null,
  subtype: null,
  isError: null,
  durationMs: null,
  numTurns: null,
  totalCostUsd: null,
  inputTokens: null,
  outputTokens: null,
  lastActivityAt: null,
  hasStoredLog: false,
  cancelRequestedAt: null,
  publish: { headSha: 'abc1234', headCommitUrl: null, pullRequestUrl: 'https://github.com/acme/api/pull/9', branchCommitCount: 1 },
  publishRetry: null,
  discardsAt: null,
  secretFindings: [],
  revertedFiles: [],
};

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
          recentToolsForRun: () => [],
          connectionStatus: signal('connected'),
          activityFor: () => null,
          logStreamFor: () => null,
          streamLog: () => of(),
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

  it('renders the header, then the status, then the facts', async () => {
    // Arrange
    const { el, httpMock, render } = await setup();

    // Act
    httpMock.expectOne('/api/issues/f').flush({ ...detail, state: 'queued' });
    await render();

    // Assert
    const layers = Array.from(el.querySelectorAll('fd-pane-header, fd-pane-status, fd-pane-facts, fd-pane-activity')).map((n) =>
      n.tagName.toLowerCase());
    expect(layers).toEqual(['fd-pane-header', 'fd-pane-status', 'fd-pane-facts']);
    expect(el.querySelector('fd-pane-facts dl')).not.toBeNull();
  });

  it('renders the activity after the facts while a worker runs', async () => {
    // Arrange
    const { el, httpMock, render } = await setup();

    // Act
    httpMock.expectOne('/api/issues/f').flush(detail);
    await render();

    // Assert
    const layers = Array.from(el.querySelectorAll('fd-pane-header, fd-pane-status, fd-pane-facts, fd-pane-activity')).map((n) =>
      n.tagName.toLowerCase());
    expect(layers).toEqual(['fd-pane-header', 'fd-pane-status', 'fd-pane-facts', 'fd-pane-activity']);
    expect(el.querySelector('.pane-callout__title')?.textContent?.trim()).toBe('Implementing');
  });

  it.each(['publishing', 'retrying'] as const)('hides the activity while the run is %s', async (publishPhase) => {
    // Arrange
    const { el, host, httpMock, render } = await setup();
    host.summary.set({ ...summary, publishPhase });

    // Act
    httpMock.expectOne('/api/issues/f').flush(detail);
    await render();

    // Assert
    expect(el.querySelector('fd-pane-activity')).toBeNull();
  });

  it('removes the activity once the run ends', async () => {
    // Arrange
    const { el, httpMock, render, service } = await setup();
    httpMock.expectOne('/api/issues/f').flush(detail);
    await render();

    // Act
    service.loadDetail('f');
    httpMock.expectOne('/api/issues/f').flush({ ...detail, state: 'review' });
    await render();

    // Assert
    expect(el.querySelector('fd-pane-activity')).toBeNull();
  });

  it('shows the facts skeleton under the status skeleton while the detail loads', async () => {
    // Act
    const { el, httpMock } = await setup();

    // Assert
    httpMock.expectOne('/api/issues/f');
    expect(el.querySelector('fd-pane-status [aria-busy="true"]')).not.toBeNull();
    expect(el.querySelector('fd-pane-facts [aria-busy="true"]')).not.toBeNull();
    expect(el.querySelector('fd-pane-activity')).toBeNull();
  });

  it('shows a load failure in the status with a Retry that reloads the detail', async () => {
    // Arrange
    const { el, httpMock, render } = await setup();
    httpMock.expectOne('/api/issues/f').flush('boom', { status: 500, statusText: 'Server Error' });
    await render();

    // Act
    (el.querySelector('fd-pane-status [role="alert"] button') as HTMLButtonElement).click();

    // Assert
    httpMock.expectOne('/api/issues/f');
    expect(el.querySelector('fd-pane-status [role="alert"]')?.textContent).toContain('Failed to load issue details');
  });

  it('loads the worker run for the status and links its pull request in the header', async () => {
    // Arrange
    const { el, httpMock, render } = await setup();

    // Act
    httpMock.expectOne('/api/issues/f').flush({ ...detail, stateDetails: {
      workerRunId: 'run-1', branchName: null, feedbackCutoffAt: null, failureReason: null, failedAt: null,
      completedAt: null, blockedBy: null, violations: null, transientRetry: null,
    } });
    await render();
    httpMock.expectOne('/api/workers/runs/run-1').flush(runningRun);
    await render();

    // Assert
    expect(el.querySelector('fd-pane-actions button')?.textContent?.trim()).toBe('Stop worker');
    const pr = Array.from(el.querySelectorAll('.pane-header__links a')).find((a) => a.textContent?.trim() === 'View PR');
    expect(pr?.getAttribute('href')).toBe('https://github.com/acme/api/pull/9');
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
    expect(el.querySelector('fd-pane-facts')).toBeNull();
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
