import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AgentActivity, AgentActivityEnded } from '../../../core/models/agent-activity.model';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { WORKER_HUB_FACTORY, WorkerHub } from '../../../core/services/worker-signalr.service';
import { FloorPageComponent } from './floor-page';

interface CapturedHub {
  onAgentActivity: ((activity: AgentActivity) => void) | null;
  onAgentActivityEnded: ((ended: AgentActivityEnded) => void) | null;
  onReconnecting: (() => void) | null;
}

function buildHub(captured: CapturedHub): WorkerHub {
  return {
    on: <T>(method: string, cb: (payload: T) => void) => {
      if (method === 'AgentActivity') {
        captured.onAgentActivity = cb as unknown as (activity: AgentActivity) => void;
      } else if (method === 'AgentActivityEnded') {
        captured.onAgentActivityEnded = cb as unknown as (ended: AgentActivityEnded) => void;
      }
    },
    onReconnected: () => {},
    onReconnecting: (cb) => { captured.onReconnecting = cb; },
    onClose: () => {},
    stream: () => ({ subscribe: () => ({ dispose: () => {} }) }),
    start: () => Promise.resolve(),
  };
}

function buildActivity(workerRunId: string, issueId: string): AgentActivity {
  return {
    workerRunId,
    issueId,
    role: 'implement',
    lastEventAt: null,
    tasks: [],
    currentTask: null,
    completedTaskCount: 0,
    subagents: [],
    activeSkill: null,
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0 },
  };
}

function issueItem(id: string, issueNumber: number, state: string) {
  return {
    id,
    issueNumber,
    title: `Issue ${issueNumber}`,
    state,
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: `https://github.com/owner/repo/issues/${issueNumber}`,
    failureClassification: null,
    repositoryEligibilityStatus: 'eligible',
    runStats: null,
  };
}

async function setup() {
  const captured: CapturedHub = { onAgentActivity: null, onAgentActivityEnded: null, onReconnecting: null };
  TestBed.configureTestingModule({
    imports: [FloorPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: WORKER_HUB_FACTORY, useValue: () => buildHub(captured) },
      {
        provide: IssueSignalRService,
        useValue: { on: () => {}, onReconnected: () => {}, connectionStatus: signal('connected') },
      },
    ],
  });
  const fixture = TestBed.createComponent(FloorPageComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  await Promise.resolve();
  fixture.detectChanges();
  return { fixture, httpMock, captured, el: fixture.nativeElement as HTMLElement };
}

function flushIssues(httpMock: HttpTestingController, items: unknown[]): void {
  httpMock.expectOne((req) => req.url === '/api/issues').flush({ items, nextCursor: null });
}

function flushCapacity(httpMock: HttpTestingController, freeSlots: number, maxConcurrent = 3): void {
  for (const req of httpMock.match('/api/workers/capacity')) {
    req.flush({ maxConcurrent, occupiedSlots: maxConcurrent - freeSlots, freeSlots });
  }
}

describe('FloorPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should render one lane per active run and free slots from server capacity', async () => {
    // Arrange
    const { fixture, httpMock, captured, el } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress'), issueItem('issue-2', 2, 'in_progress')]);
    captured.onAgentActivity!(buildActivity('run-1', 'issue-1'));
    captured.onAgentActivity!(buildActivity('run-2', 'issue-2'));
    fixture.detectChanges();

    // Act
    flushCapacity(httpMock, 1);
    fixture.detectChanges();

    // Assert
    expect(el.querySelectorAll('fd-floor-lane').length).toBe(2);
    expect(el.querySelectorAll('.floor__slot--free').length).toBe(1);
    expect(el.querySelector('.floor__summary')?.textContent).toContain('2 runs active · 1 free slot of 3');
  });

  it('should remove a lane when its run ends', async () => {
    // Arrange
    const { fixture, httpMock, captured, el } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress')]);
    captured.onAgentActivity!(buildActivity('run-1', 'issue-1'));
    fixture.detectChanges();
    flushCapacity(httpMock, 2);

    // Act
    captured.onAgentActivityEnded!({ workerRunId: 'run-1', issueId: 'issue-1' });
    fixture.detectChanges();
    flushCapacity(httpMock, 3);
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('fd-floor-lane')).toBeNull();
    expect(el.querySelector('.floor__slot--starting')).toBeNull();
  });

  it('should show occupied slots without agent activity yet as starting', async () => {
    // Arrange
    const { fixture, httpMock, captured, el } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress'), issueItem('issue-2', 2, 'in_progress')]);
    captured.onAgentActivity!(buildActivity('run-1', 'issue-1'));
    fixture.detectChanges();

    // Act — the server counts two occupied slots, but only one run reports activity.
    flushCapacity(httpMock, 1);
    fixture.detectChanges();

    // Assert
    expect(el.querySelectorAll('fd-floor-lane').length).toBe(1);
    expect(el.querySelectorAll('.floor__slot--starting').length).toBe(1);
    expect(el.querySelectorAll('.floor__slot--free').length).toBe(1);
  });

  it('should refetch capacity when the number of active runs changes', async () => {
    // Arrange
    const { fixture, httpMock, captured } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress')]);
    fixture.detectChanges();
    flushCapacity(httpMock, 2);

    // Act
    captured.onAgentActivity!(buildActivity('run-1', 'issue-1'));
    fixture.detectChanges();

    // Assert
    expect(httpMock.match('/api/workers/capacity').length).toBe(1);
  });

  it('should list the next queued issues in dispatch order', async () => {
    // Arrange
    const { fixture, httpMock, el } = await setup();

    // Act — server order is dispatch order (ADR 0067).
    flushIssues(httpMock, [
      issueItem('issue-9', 9, 'revision_queued'),
      issueItem('issue-3', 3, 'queued'),
      issueItem('issue-5', 5, 'queued'),
    ]);
    flushCapacity(httpMock, 3);
    fixture.detectChanges();

    // Assert
    const numbers = Array.from(el.querySelectorAll('.floor__queue-number')).map((n) => n.textContent);
    expect(numbers).toEqual(['#9', '#3', '#5']);
  });


  it('should tag a triage_queued row in Up next with the triage role', async () => {
    // Arrange
    const { fixture, httpMock, el } = await setup();

    // Act
    flushIssues(httpMock, [issueItem('issue-51', 51, 'triage_queued'), issueItem('issue-48', 48, 'queued')]);
    flushCapacity(httpMock, 3);
    fixture.detectChanges();

    // Assert
    const items = Array.from(el.querySelectorAll('.floor__queue-item'));
    expect(items.length).toBe(2);
    expect(items[0].querySelector('fd-role-tag')?.textContent).toContain('triage');
    expect(items[1].querySelector('fd-role-tag')).toBeNull();
  });
  it('should show an empty state with free slots when no runs are active', async () => {
    // Arrange
    const { fixture, httpMock, el } = await setup();

    // Act
    flushIssues(httpMock, []);
    flushCapacity(httpMock, 3);
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('fd-floor-lane')).toBeNull();
    expect(el.querySelectorAll('.floor__slot--free').length).toBe(3);
    expect(el.querySelector('.floor__queue')?.textContent).toContain('The queue is empty.');
  });

  it('should mark lanes stale while the worker hub is reconnecting', async () => {
    // Arrange
    const { fixture, httpMock, captured, el } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress')]);
    captured.onAgentActivity!(buildActivity('run-1', 'issue-1'));
    fixture.detectChanges();
    flushCapacity(httpMock, 2);

    // Act
    captured.onReconnecting!();
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.floor-lane--stale')).not.toBeNull();
  });
});
