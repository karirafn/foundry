import { Component, input, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AgentActivity, AgentTaskItem } from '../../../../core/models/agent-activity.model';
import { WorkerSignalRService } from '../../../../core/services/worker-signalr.service';
import { LifecycleStep } from '../../../issues/lifecycle.model';
import { PaneLifecycleComponent } from './pane-lifecycle';

// A full merge-on path, implementing in round 2, with a step this dashboard has no copy for.
const LIFECYCLE: readonly LifecycleStep[] = [
  { step: 'detected', actor: 'code', status: 'done', phase: null },
  { step: 'triage_queued', actor: 'code', status: 'done', phase: null },
  { step: 'triage', actor: 'claude', status: 'done', phase: null },
  { step: 'queued', actor: 'code', status: 'done', phase: null },
  { step: 'implement', actor: 'claude', status: 'current', phase: 'running' },
  { step: 'publish', actor: 'code', status: 'upcoming', phase: null },
  { step: 'rebase', actor: 'code', status: 'upcoming', phase: null },
  { step: 'checks', actor: 'code', status: 'upcoming', phase: null },
  { step: 'review_queued', actor: 'code', status: 'upcoming', phase: null },
  { step: 'review', actor: 'claude', status: 'upcoming', phase: null },
  { step: 'your_review', actor: 'person', status: 'upcoming', phase: null },
  {
    step: 'a_future_step_whose_label_is_long_enough_to_wrap_in_a_narrow_pane',
    actor: 'person',
    status: 'upcoming',
    phase: null,
  },
  { step: 'merge', actor: 'code', status: 'off', phase: null },
  { step: 'done', actor: 'code', status: 'upcoming', phase: null },
];

const TASKS: readonly AgentTaskItem[] = Array.from({ length: 30 }, (_, i) => ({
  id: `${i + 1}`,
  subject: `Task ${i + 1}: a subject long enough that it has to truncate inside the pane`,
  activeForm: `Working on task ${i + 1}, a description long enough to truncate as well`,
  status: i < 20 ? 'completed' : i === 20 ? 'in_progress' : 'pending',
}));

const ACTIVITY: AgentActivity = {
  workerRunId: 'run-1',
  issueId: 'abc',
  role: 'implement',
  lastEventAt: null,
  tasks: TASKS,
  currentTask: TASKS[20],
  completedTaskCount: 20,
  subagents: [{ subagentType: 'developer', description: null, lastTool: null }],
  inFlightTool: null,
  tokens: { inputTokens: 0, outputTokens: null },
  backgroundTasks: [],
};

@Component({
  selector: 'fd-pane-lifecycle-layout-host',
  imports: [PaneLifecycleComponent],
  template: `<div class="host" [style.width.px]="width()">
    <fd-pane-lifecycle
      issueId="abc"
      [lifecycle]="lifecycle"
      [round]="2"
      enteredAt="2026-01-01T00:00:00Z"
    />
  </div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly lifecycle = LIFECYCLE;
}

async function render(width: number): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: WorkerSignalRService,
        useValue: { agentActivityForIssue: signal(ACTIVITY).asReadonly() },
      },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

function box(el: Element | null): DOMRect {
  return el!.getBoundingClientRect();
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
// The pane's inner width: 576px and 640px sheets less 24px padding each side, and a 320px viewport.
describe('pane lifecycle layout', () => {
  it.each([528, 592, 272])('fits %ipx without scrolling sideways', async (width) => {
    // Act
    const host = await render(width);

    // Assert
    const right = box(host).right + 0.5;
    const outside = Array.from(host.querySelectorAll('*')).filter(
      (el) => !el.closest('.sr-only') && box(el).width > 0 && box(el).right > right,
    );
    expect(outside.map((el) => el.className)).toEqual([]);
    expect(host.scrollWidth).toBeLessThanOrEqual(host.clientWidth);
  });

  it.each([528, 592])('keeps each status on its step row at %ipx', async (width) => {
    // Act
    const host = await render(width);

    // Assert
    const rows = Array.from(host.querySelectorAll('li.rail__step'));
    const misplaced = rows.filter((li) => {
      const label = box(li.querySelector('.rail__label'));
      return box(li.querySelector('.rail__status')).top >= label.bottom;
    });
    expect(misplaced.map((li) => li.getAttribute('aria-label'))).toEqual([]);
  });

  it('wraps labels and drops the status under them at 272px', async () => {
    // Act
    const host = await render(272);

    // Assert
    const long = host.querySelector('li.rail__step[aria-label^="A future step"]')!;
    const label = box(long.querySelector('.rail__label'));
    expect(label.height).toBeGreaterThan(20);
    expect(box(long.querySelector('.rail__status')).top).toBeGreaterThanOrEqual(label.bottom - 0.5);
  });

  it('lines every node up on one axis, with the line running between them', async () => {
    // Act
    const host = await render(528);

    // Assert
    const nodes = Array.from(host.querySelectorAll('.rail__node'));
    const centres = new Set(nodes.map((n) => Math.round(box(n).left + box(n).width / 2)));
    expect(centres.size).toBe(1);
    const implement = host.querySelector('li[aria-current="step"]')!;
    const line = getComputedStyle(implement, '::before');
    expect(line.width).toBe('2px');
    expect(parseFloat(line.top)).toBeCloseTo(
      box(implement.querySelector('.rail__node')).height + 3,
      0,
    );
  });

  it('nests the task list under the current step, scrolling past 14rem', async () => {
    // Act
    const host = await render(528);

    // Assert
    const current = host.querySelector('li[aria-current="step"]')!;
    const list = current.querySelector<HTMLElement>('.task-list')!;
    expect(box(list).left).toBeGreaterThan(box(current.querySelector('.rail__node')).right);
    expect(box(list).height).toBeLessThanOrEqual(14 * 16 + 0.5);
    expect(list.scrollHeight).toBeGreaterThan(list.clientHeight);
    expect(box(current.querySelector('.pane-lifecycle__toggle')).height).toBeGreaterThanOrEqual(32);
  });
});
