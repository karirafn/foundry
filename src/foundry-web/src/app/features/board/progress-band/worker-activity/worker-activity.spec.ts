import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AgentActivity, AgentTaskItem } from '../../../../core/models/agent-activity.model';
import { WorkerActivityComponent } from './worker-activity';
import { activityPanel } from './worker-activity.model';

function task(id: string, status: string): AgentTaskItem {
  return { id, subject: `Task ${id}`, activeForm: `Doing task ${id}`, status };
}

/** Three tasks with the first `done` completed and the next one in progress. */
function activity(done: number): AgentActivity {
  const tasks = [0, 1, 2].map((i) =>
    task(`${i + 1}`, i < done ? 'completed' : i === done ? 'in_progress' : 'pending'),
  );
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    role: 'implement',
    lastEventAt: null,
    tasks,
    currentTask: tasks[done] ?? null,
    completedTaskCount: done,
    subagents: [],
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: null },
    backgroundTasks: [],
  };
}

function setup(
  source: AgentActivity | null,
  expanded = false,
): ComponentFixture<WorkerActivityComponent> {
  const fixture = TestBed.createComponent(WorkerActivityComponent);
  fixture.componentRef.setInput('panel', activityPanel(source));
  fixture.componentRef.setInput('taskLineId', 'row-task');
  fixture.componentRef.setInput('listId', 'row-tasks');
  fixture.componentRef.setInput('expanded', expanded);
  fixture.detectChanges();
  return fixture;
}

function toggle(fixture: ComponentFixture<WorkerActivityComponent>): HTMLButtonElement | null {
  return fixture.nativeElement.querySelector('.worker-activity__toggle');
}

function list(fixture: ComponentFixture<WorkerActivityComponent>): HTMLElement | null {
  return fixture.nativeElement.querySelector('fd-task-list');
}

describe('WorkerActivityComponent', () => {
  it('offers a collapsed Tasks toggle that names the checklist', () => {
    // Act
    const fixture = setup(activity(1));

    // Assert
    const button = toggle(fixture)!;
    expect(button.textContent?.trim()).toBe('Tasks');
    expect(button.getAttribute('type')).toBe('button');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.getAttribute('aria-controls')).toBe('row-tasks');
    expect(fixture.nativeElement.querySelector('#row-tasks')).not.toBeNull();
  });

  it('keeps the collapsed checklist hidden', () => {
    // Act
    const fixture = setup(activity(1));

    // Assert
    expect(list(fixture)!.hidden).toBe(true);
  });

  it('shows the checklist while expanded', () => {
    // Act
    const fixture = setup(activity(1), true);

    // Assert
    expect(toggle(fixture)!.getAttribute('aria-expanded')).toBe('true');
    expect(list(fixture)!.hidden).toBe(false);
    expect(list(fixture)!.querySelectorAll('li').length).toBe(3);
  });

  it('reports a click on the toggle', () => {
    // Arrange
    const fixture = setup(activity(1));
    let toggled = 0;
    fixture.componentInstance.toggled.subscribe(() => toggled++);

    // Act
    toggle(fixture)!.click();

    // Assert
    expect(toggled).toBe(1);
  });

  it('shows no toggle or checklist before the first event', () => {
    // Act
    const fixture = setup(null);

    // Assert
    expect(toggle(fixture)).toBeNull();
    expect(list(fixture)).toBeNull();
  });

  it('shows no toggle or checklist for a run with no task list', () => {
    // Act
    const fixture = setup({ ...activity(0), tasks: [], currentTask: null });

    // Assert
    expect(toggle(fixture)).toBeNull();
    expect(list(fixture)).toBeNull();
  });

  it('fills a segment once when its task completes, never on first render', () => {
    // Arrange
    const fixture = setup(activity(1));
    const filling = (): boolean[] =>
      Array.from(fixture.nativeElement.querySelectorAll('.worker-activity__segment')).map((s) =>
        (s as HTMLElement).classList.contains('worker-activity__segment--fill'),
      );
    expect(filling()).toEqual([false, false, false]);

    // Act
    fixture.componentRef.setInput('panel', activityPanel(activity(2)));
    fixture.detectChanges();

    // Assert
    expect(filling()).toEqual([false, true, false]);
  });
});
