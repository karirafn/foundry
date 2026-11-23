import { TestBed } from '@angular/core/testing';
import { LastEventComponent } from './last-event';
import { LastEventLine } from './worker-activity.model';

function render(line: LastEventLine) {
  TestBed.configureTestingModule({ imports: [LastEventComponent] });
  const fixture = TestBed.createComponent(LastEventComponent);
  fixture.componentRef.setInput('line', line);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function text(el: Element | null): string {
  return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

describe('LastEventComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows a recent last event as plain text without a warning', () => {
    // Act
    const el = render({ kind: 'recent', text: 'last event 42s ago' });

    // Assert
    const event = el.querySelector('.last-event__event')!;
    expect(text(event)).toBe('last event 42s ago');
    expect(event.classList).not.toContain('last-event__event--silent');
    expect(el.querySelector('svg')).toBeNull();
    expect(el.querySelector('.last-event__waiting')).toBeNull();
  });

  it('shows silence as a warning with a hidden icon', () => {
    // Act
    const el = render({ kind: 'silent', text: 'silent 6m · Bash running' });

    // Assert
    const event = el.querySelector('.last-event__event')!;
    expect(text(event)).toBe('silent 6m · Bash running');
    expect(event.classList).toContain('last-event__event--silent');
    const icon = event.querySelector('svg.last-event__warning')!;
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(icon.getAttribute('focusable')).toBe('false');
  });

  it('shows the waiting line with its command, count, waited time and hover title, hidden from screen readers', () => {
    // Act
    const el = render({
      kind: 'waiting',
      command: 'dotnet test',
      more: '+2 more',
      elapsed: '7m',
      title: 'dotnet test\nnpm test\na background task',
      srText: 'waiting on 3 background tasks, 7m',
    });

    // Assert
    const waiting = el.querySelector('.last-event__waiting')!;
    expect(waiting.getAttribute('aria-hidden')).toBe('true');
    expect(waiting.getAttribute('title')).toBe('dotnet test\nnpm test\na background task');
    expect(text(waiting)).toBe('waiting on dotnet test +2 more · 7m');
    expect(text(waiting.querySelector('.last-event__command'))).toBe('dotnet test');
    expect(text(waiting.querySelector('.last-event__more'))).toBe('+2 more');
    expect(text(waiting.querySelector('.last-event__waited'))).toBe('· 7m');
    expect(text(el.querySelector('.sr-only:last-child'))).toBe('waiting on 3 background tasks, 7m');
    expect(el.querySelector('.last-event__event')).toBeNull();
  });

  it('leaves out the count when it waits on a single task', () => {
    // Act
    const el = render({
      kind: 'waiting',
      command: 'dotnet test',
      more: null,
      elapsed: '7m',
      title: 'dotnet test',
      srText: 'waiting on 1 background task, 7m',
    });

    // Assert
    expect(el.querySelector('.last-event__more')).toBeNull();
    expect(text(el.querySelector('.last-event__waiting'))).toBe('waiting on dotnet test · 7m');
  });
});
