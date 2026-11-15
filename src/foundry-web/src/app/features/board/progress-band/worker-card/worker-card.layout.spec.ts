import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AgentActivity, AgentTaskItem } from '../../../../core/models/agent-activity.model';
import { WorkerSignalRService } from '../../../../core/services/worker-signalr.service';
import { ItemView } from '../../board-state.service';
import { WorkerItem } from '../../board.model';
import { WorkerCardComponent } from './worker-card';

const NOW = Date.parse('2026-01-01T12:00:00Z');

const ITEM: ItemView<WorkerItem> = {
  band: 'workers',
  role: 'implement',
  issue: {
    id: 'issue-1',
    issueNumber: 1,
    title: 'An issue',
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/1',
    repositoryEligibilityStatus: 'eligible',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    codeStep: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T11:50:00Z',
  },
  stage: 'implement',
  phase: 'live',
  arrived: false,
  moved: false,
};

/** `total` tasks, half of them completed and the next one in progress. */
function activityWithTasks(total: number): AgentActivity {
  const done = Math.floor(total / 2);
  const tasks: AgentTaskItem[] = Array.from({ length: total }, (_, i) => ({
    id: `${i + 1}`,
    subject: `Task ${i + 1}`,
    activeForm: null,
    status: i < done ? 'completed' : i === done ? 'in_progress' : 'pending',
  }));
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    role: 'implement',
    lastEventAt: '2026-01-01T11:59:50Z',
    tasks,
    currentTask: tasks[done],
    completedTaskCount: done,
    subagents: [],
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0 },
    backgroundTasks: [],
  };
}

@Component({
  selector: 'fd-worker-card-layout-host',
  imports: [WorkerCardComponent],
  template: `<div style="width: 20rem"><fd-worker-card [item]="item" [now]="now" /></div>`,
})
class HostComponent {
  readonly item = ITEM;
  readonly now = NOW;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('worker card layout', () => {
  it.each([20, 21, 100])(
    'keeps the task bar for %i tasks beside its label, inside the card',
    async (total) => {
      // Arrange
      const activity = activityWithTasks(total);
      TestBed.configureTestingModule({
        providers: [
          {
            provide: WorkerSignalRService,
            useValue: { activeAgentCount: signal(1), agentActivityForIssue: () => activity },
          },
        ],
      });
      const fixture = TestBed.createComponent(HostComponent);
      await fixture.whenStable();
      const root = fixture.nativeElement as HTMLElement;

      // Act
      const card = root.querySelector('.worker-card')!.getBoundingClientRect();
      const label = root.querySelector('.worker-activity__progress')!.getBoundingClientRect();
      const steps = root.querySelector('.worker-activity__steps')!;
      // The bar's own box can shrink while its children overflow it, so take the rightmost of all.
      const barRight = Math.max(
        ...Array.from(steps.querySelectorAll('[aria-hidden="true"], [aria-hidden="true"] *')).map(
          (el) => el.getBoundingClientRect().right,
        ),
      );

      // Assert
      expect(label.width).toBeGreaterThan(0);
      expect(barRight).toBeLessThanOrEqual(label.left);
      expect(label.right).toBeLessThanOrEqual(card.right);
    },
  );
});
