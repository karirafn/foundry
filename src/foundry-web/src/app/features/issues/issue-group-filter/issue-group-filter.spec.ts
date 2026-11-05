import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { IssueGroupFilterComponent } from './issue-group-filter';
import { IssueService } from '../issue.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { STATE_GROUPS, StateGroupId } from '../issue-lifecycle.model';

function createMockSignalR() {
  return {
    on: () => {},
    onReconnected: () => {},
  };
}

function setup() {
  TestBed.configureTestingModule({
    imports: [IssueGroupFilterComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: IssueSignalRService, useValue: createMockSignalR() },
    ],
  });

  const fixture = TestBed.createComponent(IssueGroupFilterComponent);
  const service = TestBed.inject(IssueService);
  const httpMock = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  return { fixture, service, httpMock, el: fixture.nativeElement as HTMLElement };
}

function chip(el: HTMLElement, id: StateGroupId): HTMLButtonElement {
  const button = el.querySelector<HTMLButtonElement>(`button[data-group="${id}"]`);
  if (button === null) {
    throw new Error(`chip ${id} not rendered`);
  }
  return button;
}

function flushCounts(httpMock: HttpTestingController, service: IssueService, counts: Record<string, number>): void {
  service.loadCounts();
  httpMock.expectOne('/api/issues/counts').flush({ counts });
}

describe('IssueGroupFilterComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).match(() => true);
  });

  it('should render one chip per group in STATE_GROUPS order inside a labelled group', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const group = el.querySelector('[role="group"]');
    expect(group?.getAttribute('aria-label')).toBe('Filter issues by group');
    const labels = Array.from(el.querySelectorAll('.group-filter__label')).map(l => l.textContent?.trim());
    expect(labels).toEqual(STATE_GROUPS.map(g => g.label));
    expect(labels).toEqual(['In progress', 'Needs attention', 'Waiting', 'Resolved']);
  });

  it('should press In progress, Needs attention and Waiting but not Resolved on a fresh load', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    expect(chip(el, 'in_progress').getAttribute('aria-pressed')).toBe('true');
    expect(chip(el, 'needs_attention').getAttribute('aria-pressed')).toBe('true');
    expect(chip(el, 'waiting').getAttribute('aria-pressed')).toBe('true');
    expect(chip(el, 'resolved').getAttribute('aria-pressed')).toBe('false');
  });

  it('should toggle a chip independently of the others and reflect it in aria-pressed', () => {
    // Arrange
    const { fixture, el } = setup();

    // Act
    chip(el, 'waiting').click();
    fixture.detectChanges();

    // Assert
    expect(chip(el, 'waiting').getAttribute('aria-pressed')).toBe('false');
    expect(chip(el, 'in_progress').getAttribute('aria-pressed')).toBe('true');
    expect(chip(el, 'needs_attention').getAttribute('aria-pressed')).toBe('true');
    expect(chip(el, 'resolved').getAttribute('aria-pressed')).toBe('false');

    // Act — toggle back on
    chip(el, 'waiting').click();
    fixture.detectChanges();

    // Assert
    expect(chip(el, 'waiting').getAttribute('aria-pressed')).toBe('true');
  });

  it('should allow several chips to be pressed at once (multi-select)', () => {
    // Arrange
    const { fixture, el, service } = setup();

    // Act
    chip(el, 'resolved').click();
    fixture.detectChanges();

    // Assert
    expect([...service.selectedGroups()].sort()).toEqual(['in_progress', 'needs_attention', 'resolved', 'waiting']);
    expect(el.querySelectorAll('button[aria-pressed="true"]').length).toBe(4);
  });

  it('should show the sum of each group\'s state counts', () => {
    // Arrange
    const { fixture, el, service, httpMock } = setup();

    // Act
    flushCounts(httpMock, service, {
      in_progress: 2,
      revision_in_progress: 1,
      review: 4,
      failed: 1,
      queued: 3,
      blocked: 2,
      checks_pending: 1,
      completed: 10,
    });
    fixture.detectChanges();

    // Assert
    expect(chip(el, 'in_progress').querySelector('.group-filter__count')?.textContent?.trim()).toBe('3');
    expect(chip(el, 'needs_attention').querySelector('.group-filter__count')?.textContent?.trim()).toBe('5');
    expect(chip(el, 'waiting').querySelector('.group-filter__count')?.textContent?.trim()).toBe('6');
    expect(chip(el, 'resolved').querySelector('.group-filter__count')?.textContent?.trim()).toBe('10');
  });

  it('should announce the count in the chip aria-label with singular and plural units', () => {
    // Arrange
    const { fixture, el, service, httpMock } = setup();

    // Act
    flushCounts(httpMock, service, { in_progress: 1, review: 2 });
    fixture.detectChanges();

    // Assert
    expect(chip(el, 'in_progress').getAttribute('aria-label')).toBe('In progress, 1 issue');
    expect(chip(el, 'needs_attention').getAttribute('aria-label')).toBe('Needs attention, 2 issues');
  });

  it('should dim a zero-count chip, announce "no issues", and keep it focusable and toggleable', () => {
    // Arrange
    const { fixture, el, service, httpMock } = setup();
    flushCounts(httpMock, service, { in_progress: 1 });
    fixture.detectChanges();
    const waiting = chip(el, 'waiting');

    // Assert — dimmed, announced, not disabled
    expect(waiting.classList.contains('group-filter__chip--empty')).toBe(true);
    expect(waiting.getAttribute('aria-label')).toBe('Waiting, no issues');
    expect(waiting.disabled).toBe(false);
    expect(waiting.hasAttribute('tabindex')).toBe(false);
    expect(chip(el, 'in_progress').classList.contains('group-filter__chip--empty')).toBe(false);

    // Act — still toggleable
    waiting.click();
    fixture.detectChanges();

    // Assert
    expect(waiting.getAttribute('aria-pressed')).toBe('false');
  });

  it('should show 0 dimmed on every chip and stay functional when the counts fetch fails', () => {
    // Arrange
    const { fixture, el, service, httpMock } = setup();
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

    // Act
    service.loadCounts();
    httpMock.expectOne('/api/issues/counts').flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    // Assert
    for (const group of STATE_GROUPS) {
      const button = chip(el, group.id);
      expect(button.querySelector('.group-filter__count')?.textContent?.trim()).toBe('0');
      expect(button.classList.contains('group-filter__chip--empty')).toBe(true);
      expect(button.disabled).toBe(false);
    }

    chip(el, 'in_progress').click();
    fixture.detectChanges();
    expect(chip(el, 'in_progress').getAttribute('aria-pressed')).toBe('false');
    consoleSpy.mockRestore();
  });

  it('should start the resolved fetch when Resolved is pressed', () => {
    // Arrange
    const { el, httpMock } = setup();

    // Act
    chip(el, 'resolved').click();

    // Assert
    const req = httpMock.expectOne(r => r.url === '/api/issues' && r.params.getAll('states')?.includes('completed') === true);
    expect(req.request.method).toBe('GET');
  });
});
