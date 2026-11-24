import { Component, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AgentActivity, AgentTaskItem } from '../../../core/models/agent-activity.model';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { IssueSummary } from '../../issues/issue.model';
import { ItemView } from '../board-state.service';
import { WorkerItem } from '../board.model';
import { LifecycleRowComponent } from './lifecycle-row';

const TASKS: readonly AgentTaskItem[] = [
  { id: '1', subject: 'Read', activeForm: 'Reading', status: 'completed' },
  { id: '2', subject: 'Write tests', activeForm: 'Writing tests', status: 'in_progress' },
];

function activity(): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-7',
    role: 'implement',
    lastEventAt: null,
    tasks: TASKS,
    currentTask: TASKS[1],
    completedTaskCount: 1,
    subagents: [],
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: null },
    backgroundTasks: [],
  };
}

function workerItem(): ItemView<WorkerItem> {
  const issue: IssueSummary = {
    id: 'issue-7',
    issueNumber: 7,
    title: 'Open PRs as drafts',
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/7',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    runKind: 'fresh',
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
    lifecycle: [{ step: 'implement', actor: 'claude', status: 'current', phase: 'running' }],
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
  selector: 'fd-lifecycle-row-host',
  imports: [LifecycleRowComponent],
  template: `<fd-lifecycle-row
    [item]="item"
    [now]="0"
    [tasksExpanded]="expanded()"
    (tasksToggled)="toggles = toggles + 1"
    (open)="opens = opens + 1"
  />`,
})
class HostComponent {
  readonly item = workerItem();
  readonly expanded = signal(false);
  toggles = 0;
  opens = 0;
}

interface Harness {
  fixture: ComponentFixture<HostComponent>;
  activities: WritableSignal<ReadonlyMap<string, AgentActivity>>;
}

function render(): Harness {
  const activities = signal<ReadonlyMap<string, AgentActivity>>(new Map([['issue-7', activity()]]));
  TestBed.configureTestingModule({
    providers: [
      {
        provide: WorkerSignalRService,
        useValue: {
          activeAgentCount: signal(1),
          agentActivityForIssue: (id: string) => activities().get(id) ?? null,
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  document.body.appendChild(fixture.nativeElement);
  fixture.detectChanges();
  return { fixture, activities };
}

function toggle(fixture: ComponentFixture<HostComponent>): HTMLButtonElement | null {
  return fixture.nativeElement.querySelector('.worker-activity__toggle');
}

describe('LifecycleRowComponent task list', () => {
  afterEach(() => document.body.replaceChildren());

  it("shows a running Claude step's task line, progress and toggle", () => {
    // Act
    const { fixture } = render();

    // Assert
    const row = fixture.nativeElement as HTMLElement;
    expect(row.querySelector('.worker-activity__task')?.textContent).toBe('Writing tests');
    expect(row.querySelector('.worker-activity__progress')?.textContent).toBe('task 2 of 2');
    expect(toggle(fixture)?.getAttribute('aria-controls')).toBe('lifecycle-row-issue-7-tasks');
  });

  it('reports the toggle without opening the pane', () => {
    // Arrange
    const { fixture } = render();

    // Act
    toggle(fixture)!.click();

    // Assert
    expect([fixture.componentInstance.toggles, fixture.componentInstance.opens]).toEqual([1, 0]);
  });

  it('opens the checklist while the issue is expanded', () => {
    // Arrange
    const { fixture } = render();

    // Act
    fixture.componentInstance.expanded.set(true);
    fixture.detectChanges();

    // Assert
    expect(toggle(fixture)!.getAttribute('aria-expanded')).toBe('true');
    expect((fixture.nativeElement.querySelector('fd-task-list') as HTMLElement).hidden).toBe(false);
  });

  it('moves focus from the toggle to the title when the run ends', async () => {
    // Arrange
    const { fixture, activities } = render();
    toggle(fixture)!.focus();

    // Act
    activities.set(new Map());
    fixture.detectChanges();
    await fixture.whenStable();

    // Assert
    expect(toggle(fixture)).toBeNull();
    expect(document.activeElement?.classList.contains('lifecycle-row__title')).toBe(true);
  });

  it('leaves focus alone when the run ends while focus is elsewhere', async () => {
    // Arrange
    const { fixture, activities } = render();
    const elsewhere = document.createElement('button');
    document.body.appendChild(elsewhere);
    elsewhere.focus();

    // Act
    activities.set(new Map());
    fixture.detectChanges();
    await fixture.whenStable();

    // Assert
    expect(document.activeElement).toBe(elsewhere);
  });
});
