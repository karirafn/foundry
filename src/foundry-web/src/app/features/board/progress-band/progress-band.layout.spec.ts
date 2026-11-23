import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AgentActivity, AgentTaskItem } from '../../../core/models/agent-activity.model';
import { IssueSummary } from '../../issues/issue.model';
import { DispatchCapacityService } from '../../../core/services/dispatch-capacity.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { IssueService } from '../../issues/issue.service';
import { ItemView } from '../board-state.service';
import { WorkerItem } from '../board.model';
import { ProgressBandComponent } from './progress-band';

const NOW = Date.parse('2026-01-01T12:00:00Z');

function buildIssue(id: string, title: string): IssueSummary {
  return {
    id,
    issueNumber: Number(id.replace('issue-', '')),
    title,
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: `https://github.com/acme/api/issues/${id}`,
    repositoryEligibilityStatus: 'eligible',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    codeStep: null,
    runKind: 'fresh',
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T11:50:00Z',
    round: 1,
  };
}

function worker(id: string, title: string): ItemView<WorkerItem> {
  return {
    band: 'workers',
    runKind: 'fresh',
    issue: buildIssue(id, title),
    stage: 'implement',
    phase: 'live',
    arrived: false,
    moved: false,
  };
}

function buildActivity(issueId: string, tasks: readonly AgentTaskItem[]): AgentActivity {
  const current = tasks.find((t) => t.status === 'in_progress') ?? null;
  return {
    workerRunId: `run-${issueId}`,
    issueId,
    role: 'implement',
    lastEventAt: '2026-01-01T11:59:50Z',
    tasks,
    currentTask: current,
    completedTaskCount: tasks.filter((t) => t.status === 'completed').length,
    subagents: [],
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0 },
    backgroundTasks: [],
  };
}

const LONG_TITLE =
  'A title long enough to wrap onto a second line of the worker card in a three-column row of cards';

@Component({
  selector: 'fd-progress-band-layout-host',
  imports: [ProgressBandComponent],
  // Wide enough for exactly three 20rem columns in one row.
  template: `<div style="width: 1100px">
    <fd-progress-band [workers]="workers" [steps]="[]" [queuedCount]="0" [now]="now" />
  </div>`,
})
class HostComponent {
  readonly workers: readonly ItemView<WorkerItem>[] = [
    worker('issue-1', LONG_TITLE),
    worker('issue-2', LONG_TITLE),
    worker('issue-3', 'Short title'),
  ];
  readonly now = NOW;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('progress band layout', () => {
  it('gives every worker card in a row the same height', async () => {
    // Arrange
    const activities = new Map<string, AgentActivity>([
      [
        'issue-1',
        buildActivity('issue-1', [
          { id: '1', subject: 'Task 1', activeForm: null, status: 'completed' },
          { id: '2', subject: 'Task 2', activeForm: 'Running tests', status: 'in_progress' },
          { id: '3', subject: 'Task 3', activeForm: null, status: 'pending' },
        ]),
      ],
      ['issue-2', buildActivity('issue-2', [])],
      ['issue-3', buildActivity('issue-3', [])],
    ]);
    TestBed.configureTestingModule({
      providers: [
        {
          provide: WorkerSignalRService,
          useValue: {
            activeAgentCount: signal(activities.size),
            agentActivityForIssue: (id: string) => activities.get(id) ?? null,
          },
        },
        {
          provide: DispatchCapacityService,
          useValue: {
            capacity: signal(null),
            loadError: signal(null),
            load: () => Promise.resolve('loaded'),
          },
        },
        { provide: IssueService, useValue: { liveIssueCount: signal(3) } },
      ],
    });
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();

    // Act
    const cards = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.worker-card'),
    ).map((card) => card.getBoundingClientRect());

    // Assert
    expect(cards).toHaveLength(3);
    expect(new Set(cards.map((card) => card.top)).size).toBe(1);
    expect(cards[0].height).toBeGreaterThan(0);
    expect(cards.map((card) => card.height)).toEqual([
      cards[0].height,
      cards[0].height,
      cards[0].height,
    ]);
  });
});
