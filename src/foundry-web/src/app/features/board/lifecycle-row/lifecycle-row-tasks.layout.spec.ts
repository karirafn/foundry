import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';
import { AgentActivity, AgentTaskItem } from '../../../core/models/agent-activity.model';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { IssueSummary } from '../../issues/issue.model';
import { ItemView } from '../board-state.service';
import { WorkerItem } from '../board.model';
import { LifecycleRowComponent } from './lifecycle-row';

// A long run: more tasks than segments, each with text long enough to truncate.
const TASKS: readonly AgentTaskItem[] = Array.from({ length: 30 }, (_, i) => ({
  id: `${i + 1}`,
  subject: `Task ${i + 1}: a subject long enough that it has to truncate inside a narrow row`,
  activeForm: `Working on task ${i + 1}, a description long enough to truncate as well`,
  status: i < 20 ? 'completed' : i === 20 ? 'in_progress' : 'pending',
}));

function activity(tasks: readonly AgentTaskItem[]): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-7',
    role: 'implement',
    lastEventAt: null,
    tasks,
    currentTask: tasks.find((t) => t.status === 'in_progress') ?? null,
    completedTaskCount: tasks.filter((t) => t.status === 'completed').length,
    subagents: [{ subagentType: 'developer', description: null, lastTool: null }],
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: null },
    backgroundTasks: [],
  };
}

function workerItem(): ItemView<WorkerItem> {
  const issue: IssueSummary = {
    id: 'issue-7',
    issueNumber: 7,
    title: 'A title long enough that it has to give up width before the row scrolls sideways',
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/7',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    codeStep: null,
    runKind: 'fresh',
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
    lifecycle: [
      { step: 'triage', actor: 'claude', status: 'done', phase: null },
      { step: 'implement', actor: 'claude', status: 'current', phase: 'running' },
      { step: 'publish', actor: 'code', status: 'upcoming', phase: null },
    ],
  };
  return {
    band: 'workers',
    issue,
    stage: 'implement',
    runKind: 'fresh',
    phase: 'live',
    arrived: false,
    moved: false,
  };
}

@Component({
  selector: 'fd-lifecycle-row-tasks-layout-host',
  imports: [LifecycleRowComponent],
  // The board page's own padding, so the row is as wide as it is on the page.
  template: `<div style="padding: 24px">
    <fd-lifecycle-row [item]="item" [now]="0" [tasksExpanded]="true" />
  </div>`,
})
class HostComponent {
  readonly item = workerItem();
}

function render(tasks: readonly AgentTaskItem[] = TASKS): HTMLElement {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: WorkerSignalRService,
        useValue: { activeAgentCount: signal(1), agentActivityForIssue: () => activity(tasks) },
      },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('lifecycle row task list layout', () => {
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => page.viewport(original.width, original.height));

  it.each([320, 760, 1280])(
    'fits the expanded checklist at %ipx without scrolling sideways',
    async (width) => {
      // Arrange
      await page.viewport(width, 800);

      // Act
      const el = render();

      // Assert
      const row = el.querySelector<HTMLElement>('.lifecycle-row')!;
      const list = el.querySelector<HTMLElement>('.task-list')!;
      expect(row.scrollWidth).toBeLessThanOrEqual(row.clientWidth);
      expect(list.getBoundingClientRect().right).toBeLessThanOrEqual(
        row.getBoundingClientRect().right,
      );
      expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(width);
    },
  );

  it('caps a long checklist at 14rem and scrolls it', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert
    const list = el.querySelector<HTMLElement>('.task-list')!;
    expect(list.getBoundingClientRect().height).toBeLessThanOrEqual(14 * 16 + 0.5);
    expect(list.scrollHeight).toBeGreaterThan(list.clientHeight);
  });

  it('shows the meter above 20 tasks', async () => {
    // Act
    const el = render();

    // Assert
    expect(el.querySelector('.worker-activity__meter')).not.toBeNull();
    expect(el.querySelector('.worker-activity__segment')).toBeNull();
  });

  it('keeps the toggle above the title overlay, so a click reaches the toggle', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert
    const toggle = el.querySelector<HTMLElement>('.worker-activity__toggle')!;
    const box = toggle.getBoundingClientRect();
    const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
    expect(toggle.contains(hit)).toBe(true);
    expect(box.height).toBeGreaterThanOrEqual(32);
  });

  it('drops the count and toggle under the bar at 320px', async () => {
    // Arrange
    await page.viewport(320, 800);

    // Act
    const el = render(TASKS.slice(0, 6));

    // Assert
    const bar = el.querySelector<HTMLElement>('.worker-activity__bar')!.getBoundingClientRect();
    const toggle = el
      .querySelector<HTMLElement>('.worker-activity__toggle')!
      .getBoundingClientRect();
    expect(toggle.top).toBeGreaterThanOrEqual(bar.bottom);
  });

  it('keeps the bar on one line with its count and toggle at 1280px', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render(TASKS.slice(0, 6));

    // Assert
    const bar = el.querySelector<HTMLElement>('.worker-activity__bar')!.getBoundingClientRect();
    const toggle = el
      .querySelector<HTMLElement>('.worker-activity__toggle')!
      .getBoundingClientRect();
    expect(toggle.top).toBeLessThan(bar.bottom);
    expect(toggle.left).toBeGreaterThan(bar.right);
  });
});
