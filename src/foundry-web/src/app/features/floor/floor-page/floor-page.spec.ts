import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AgentActivity, AgentActivityEnded } from '../../../core/models/agent-activity.model';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { FakeHubTransport, provideFakeHubs } from '../../../core/testing/fake-hub-transport';
import { FakeConnectionGate, provideFakeConnectionGate } from '../../../core/testing/fake-connection-gate';
import { FloorPageComponent } from './floor-page';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';

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

async function setup(fragment: string | null = null) {
  const hub = new FakeHubTransport();
  const gate = new FakeConnectionGate();
  TestBed.configureTestingModule({
    imports: [FloorPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideFakeHubs({ workers: hub }),
      provideFakeConnectionGate(gate),
      { provide: ActivatedRoute, useValue: { fragment: of(fragment) } },
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
  return { fixture, httpMock, hub, gate, el: fixture.nativeElement as HTMLElement };
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

  describe('fragment', () => {
    afterEach(() => {
      vi.restoreAllMocks();
      document.body.innerHTML = '';
    });

    it('scrolls to and focuses the lane heading the fragment names', async () => {
      // Arrange
      const scroll = vi.fn();
      Element.prototype.scrollIntoView = scroll;
      const { fixture, httpMock, hub, el } = await setup('floor-lane-issue-2');
      document.body.appendChild(el);
      flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress'), issueItem('issue-2', 2, 'in_progress')]);

      // Act
      hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
      hub.emit('AgentActivity', buildActivity('run-2', 'issue-2'));
      fixture.detectChanges();
      await fixture.whenStable();

      // Assert
      expect(document.activeElement?.id).toBe('floor-lane-issue-2');
      expect(document.activeElement?.getAttribute('tabindex')).toBe('-1');
      expect(scroll).toHaveBeenCalledWith({ block: 'start' });
    });

    it('ignores a fragment that names no lane', async () => {
      // Arrange
      const scroll = vi.fn();
      Element.prototype.scrollIntoView = scroll;
      const { fixture, httpMock, hub, el } = await setup('floor-lane-gone');
      document.body.appendChild(el);
      flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress')]);

      // Act
      hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
      fixture.detectChanges();
      await fixture.whenStable();

      // Assert
      expect(scroll).not.toHaveBeenCalled();
      expect(document.activeElement).toBe(document.body);
    });
  });

  it('should render one lane per active run and free slots from server capacity', async () => {
    // Arrange
    const { fixture, httpMock, hub, el } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress'), issueItem('issue-2', 2, 'in_progress')]);
    hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
    hub.emit('AgentActivity', buildActivity('run-2', 'issue-2'));
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
    const { fixture, httpMock, hub, el } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress')]);
    hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
    fixture.detectChanges();
    flushCapacity(httpMock, 2);

    // Act
    hub.emit('AgentActivityEnded', { workerRunId: 'run-1', issueId: 'issue-1' });
    fixture.detectChanges();
    flushCapacity(httpMock, 3);
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('fd-floor-lane')).toBeNull();
    expect(el.querySelector('.floor__slot--starting')).toBeNull();
  });

  it('should show occupied slots without agent activity yet as starting', async () => {
    // Arrange
    const { fixture, httpMock, hub, el } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress'), issueItem('issue-2', 2, 'in_progress')]);
    hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
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
    const { fixture, httpMock, hub } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress')]);
    fixture.detectChanges();
    flushCapacity(httpMock, 2);

    // Act
    hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
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

  it('should refetch the current activity, the issues and capacity when the gate reloads it', async () => {
    // Arrange
    const { httpMock, hub, gate } = await setup();
    flushIssues(httpMock, []);
    flushCapacity(httpMock, 2);
    // Let the load the hub starts on connect settle first.
    await new Promise((resolve) => setTimeout(resolve, 0));
    hub.invocations.length = 0;
    hub.invokeResult = () => Promise.resolve({ workerActivities: [], agentActivities: [] });

    // Act
    const reloading = gate.registered!();
    flushIssues(httpMock, []);
    flushCapacity(httpMock, 2);

    // Assert
    expect(await reloading).toBe('loaded');
    expect(hub.invocations.map((i) => i.methodName)).toEqual(['GetCurrentActivity']);
  });

  it('should report a failed reload when the current activity does not load', async () => {
    // Arrange
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const { httpMock, hub, gate } = await setup();
    flushIssues(httpMock, []);
    flushCapacity(httpMock, 2);
    hub.invokeResult = () => Promise.reject(new Error('down'));

    // Act
    const reloading = gate.registered!();
    flushIssues(httpMock, []);
    flushCapacity(httpMock, 2);

    // Assert
    expect(await reloading).toBe('failed');
  });

  it('should hide the lanes behind the reconnect screen while the gate is up', async () => {
    // Arrange
    const { fixture, httpMock, hub, gate, el } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress')]);
    hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
    fixture.detectChanges();

    // Act
    gate.phase.set('reconnecting');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('fd-floor-lane')).toBeNull();
    expect(el.querySelector('.floor')).toBeNull();
    expect(el.querySelector('.connection-gate__heading')?.textContent).toContain('Reconnecting to Foundry');
  });

  // Publishing slots (#787)
  it('should render a publishing card and a retrying card in place of starting slots', async () => {
    // Arrange
    const { fixture, httpMock, hub, el } = await setup();
    flushIssues(httpMock, [
      issueItem('issue-1', 1, 'in_progress'),
      { ...issueItem('issue-42', 42, 'in_progress'), title: 'Add retry', publishPhase: 'publishing' },
      { ...issueItem('issue-57', 57, 'revision_in_progress'), title: 'Fix login redirect', publishPhase: 'retrying' },
    ]);
    hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
    fixture.detectChanges();

    // Act — three occupied slots: one agent lane and two publishing runs.
    flushCapacity(httpMock, 1, 4);
    fixture.detectChanges();

    // Assert
    const cards = Array.from(el.querySelectorAll('.floor__slot--publishing'));
    expect(cards.length).toBe(2);
    expect(cards[0].classList.contains('floor__slot--publish-retrying')).toBe(false);
    expect(cards[0].textContent).toContain('Publishing');
    expect(cards[0].textContent).toContain('#42');
    expect(cards[0].textContent).toContain('Add retry');
    expect(cards[0].textContent).toContain("Pushing the run's commits");
    expect(cards[0].querySelector('a')?.getAttribute('href')).toBe('https://github.com/owner/repo/issues/42');
    expect(cards[1].classList.contains('floor__slot--publish-retrying')).toBe(true);
    expect(cards[1].textContent).toContain('Retrying publish');
    expect(cards[1].textContent).toContain('#57');
    expect(cards[1].textContent).toContain('Publishing failed; Foundry retries automatically');
    expect(el.querySelectorAll('.floor__slot--starting').length).toBe(0);
    expect(el.querySelectorAll('fd-floor-lane').length).toBe(1);
  });

  it('should place publishing cards after the agent lanes and before the starting slots', async () => {
    // Arrange
    const { fixture, httpMock, hub, el } = await setup();
    flushIssues(httpMock, [
      issueItem('issue-1', 1, 'in_progress'),
      { ...issueItem('issue-42', 42, 'in_progress'), publishPhase: 'publishing' },
    ]);
    hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
    fixture.detectChanges();

    // Act — three occupied: lane, publishing, starting.
    flushCapacity(httpMock, 1, 4);
    fixture.detectChanges();

    // Assert
    const items = Array.from(el.querySelectorAll('.floor__lanes > li'));
    expect(items[0].querySelector('fd-floor-lane')).not.toBeNull();
    expect(items[1].querySelector('.floor__slot--publishing')).not.toBeNull();
    expect(items[2].querySelector('.floor__slot--starting')).not.toBeNull();
  });

  it('should include the publishing count in the summary only when above zero', async () => {
    // Arrange
    const { fixture, httpMock, hub, el } = await setup();
    flushIssues(httpMock, [
      issueItem('issue-1', 1, 'in_progress'),
      issueItem('issue-2', 2, 'in_progress'),
      { ...issueItem('issue-42', 42, 'in_progress'), publishPhase: 'publishing' },
    ]);
    hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
    hub.emit('AgentActivity', buildActivity('run-2', 'issue-2'));
    fixture.detectChanges();

    // Act
    flushCapacity(httpMock, 1, 4);
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.floor__summary')?.textContent).toContain('2 runs active · 1 publishing · 1 free slot of 4');
  });

  it('should leave the publishing count out of the summary when nothing is publishing', async () => {
    // Arrange
    const { fixture, httpMock, hub, el } = await setup();
    flushIssues(httpMock, [issueItem('issue-1', 1, 'in_progress')]);
    hub.emit('AgentActivity', buildActivity('run-1', 'issue-1'));
    fixture.detectChanges();

    // Act
    flushCapacity(httpMock, 2);
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.floor__summary')?.textContent).not.toContain('publishing');
  });

  it('should hide the empty message while a run is publishing', async () => {
    // Arrange
    const { fixture, httpMock, el } = await setup();

    // Act
    flushIssues(httpMock, [{ ...issueItem('issue-42', 42, 'in_progress'), publishPhase: 'publishing' }]);
    flushCapacity(httpMock, 2);
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.floor__empty')).toBeNull();
    expect(el.querySelectorAll('.floor__slot--publishing').length).toBe(1);
  });

  it('should show the empty message when there are no lanes, starting or publishing slots', async () => {
    // Arrange
    const { fixture, httpMock, el } = await setup();

    // Act
    flushIssues(httpMock, []);
    flushCapacity(httpMock, 3);
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.floor__empty')?.textContent).toContain('No agents are working right now.');
  });
});
