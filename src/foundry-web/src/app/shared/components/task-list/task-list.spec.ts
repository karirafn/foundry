import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TaskListItem } from '../../utils/agent-activity-format';
import { TaskListComponent } from './task-list';

const READ: TaskListItem = { id: '1', text: 'Read the criteria', state: 'done', subagent: null };
const WRITE: TaskListItem = {
  id: '2',
  text: 'Writing failing tests',
  state: 'current',
  subagent: 'developer',
};
const RUN: TaskListItem = { id: '3', text: 'Run the suite', state: 'upcoming', subagent: null };

function setup(items: readonly TaskListItem[]): ComponentFixture<TaskListComponent> {
  const fixture = TestBed.createComponent(TaskListComponent);
  fixture.componentRef.setInput('items', items);
  fixture.componentRef.setInput('listId', 'tasks-1');
  fixture.detectChanges();
  return fixture;
}

function rows(fixture: ComponentFixture<TaskListComponent>): HTMLElement[] {
  return Array.from(fixture.nativeElement.querySelectorAll('li'));
}

function update(
  fixture: ComponentFixture<TaskListComponent>,
  items: readonly TaskListItem[],
): void {
  fixture.componentRef.setInput('items', items);
  fixture.detectChanges();
}

describe('TaskListComponent', () => {
  it('lists every task under an Agent tasks label', () => {
    // Act
    const fixture = setup([READ, WRITE, RUN]);

    // Assert
    const list = fixture.nativeElement.querySelector('ol') as HTMLElement;
    expect(list.getAttribute('aria-label')).toBe('Agent tasks');
    expect(list.id).toBe('tasks-1');
    expect(rows(fixture).map((li) => li.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Done: Read the criteria',
      'In progress: Writing failing tests, via developer subagent',
      'Pending: Run the suite',
    ]);
  });

  it('marks each task by its state', () => {
    // Act
    const fixture = setup([READ, WRITE, RUN]);

    // Assert
    expect(rows(fixture).map((li) => li.getAttribute('data-state'))).toEqual([
      'done',
      'current',
      'upcoming',
    ]);
  });

  it('shows no subagent line for a current task no subagent runs', () => {
    // Act
    const fixture = setup([{ ...WRITE, subagent: null }]);

    // Assert
    expect(fixture.nativeElement.querySelector('.task-list__via')).toBeNull();
  });

  it('keeps the scroll area reachable by keyboard', () => {
    // Act
    const fixture = setup([READ, WRITE, RUN]);

    // Assert
    expect(fixture.nativeElement.querySelector('ol').getAttribute('tabindex')).toBe('0');
  });

  it('animates nothing on first render', () => {
    // Act
    const fixture = setup([READ, WRITE, RUN]);

    // Assert
    expect(fixture.nativeElement.querySelector('.task-list__item--fresh-done')).toBeNull();
    expect(fixture.nativeElement.querySelector('.task-list__item--fresh-current')).toBeNull();
  });

  it('marks a task that completes and the task that becomes current, once', () => {
    // Arrange
    const fixture = setup([READ, WRITE, RUN]);

    // Act
    update(fixture, [READ, { ...WRITE, state: 'done' }, { ...RUN, state: 'current' }]);

    // Assert
    expect(rows(fixture).map((li) => li.className.includes('fresh-done'))).toEqual([
      false,
      true,
      false,
    ]);
    expect(rows(fixture).map((li) => li.className.includes('fresh-current'))).toEqual([
      false,
      false,
      true,
    ]);
  });

  it('keeps the marks while the same tasks arrive again', () => {
    // Arrange
    const fixture = setup([READ, WRITE, RUN]);
    const next = [
      READ,
      { ...WRITE, state: 'done' as const },
      { ...RUN, state: 'current' as const },
    ];
    update(fixture, next);

    // Act
    update(
      fixture,
      next.map((item) => ({ ...item })),
    );

    // Assert
    expect(rows(fixture)[1].className).toContain('fresh-done');
  });
});
