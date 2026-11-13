import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { DispatchService } from '../../../core/services/dispatch.service';
import { DispatchCapacity, DispatchCapacityService } from '../../floor/dispatch-capacity.service';
import { IssueService } from '../../issues/issue.service';
import { BoardSummaryComponent } from './board-summary';

function setup(
  options: {
    capacity?: DispatchCapacity | null;
    capacityError?: string | null;
    needs?: number;
    waiting?: number;
  } = {},
) {
  const load = vi.fn();
  TestBed.configureTestingModule({
    imports: [BoardSummaryComponent],
    providers: [
      provideRouter([]),
      {
        provide: DispatchCapacityService,
        useValue: {
          capacity: signal(options.capacity ?? null),
          loadError: signal(options.capacityError ?? null),
          load,
        },
      },
      { provide: WorkerSignalRService, useValue: { activeAgentCount: signal(0) } },
      { provide: IssueService, useValue: { liveIssueCount: signal(0) } },
      { provide: IssueSignalRService, useValue: { connectionStatus: signal('connected') } },
      {
        provide: DispatchService,
        useValue: {
          isDispatchPaused: signal(false),
          usageLimitResetsAt: signal(null),
          pausing: signal(false),
          resuming: signal(false),
          pauseResumeError: signal(null),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(BoardSummaryComponent);
  fixture.componentRef.setInput('waitingCount', options.waiting ?? 0);
  fixture.componentRef.setInput('needsCount', options.needs ?? 0);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement, load };
}

describe('BoardSummaryComponent', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the busy slots of the maximum', () => {
    const { el } = setup({ capacity: { maxConcurrent: 4, occupiedSlots: 3, freeSlots: 1 } });
    expect(el.querySelector('.board-summary__capacity')?.textContent?.trim()).toBe(
      '3 of 4 slots busy',
    );
  });

  it('draws one pip per slot, busy ones filled', () => {
    // Act
    const { el } = setup({ capacity: { maxConcurrent: 4, occupiedSlots: 3, freeSlots: 1 } });

    // Assert
    expect(el.querySelectorAll('.board-summary__pip')).toHaveLength(4);
    expect(el.querySelectorAll('.board-summary__pip--busy')).toHaveLength(3);
  });

  it('says capacity is unavailable when it failed to load', () => {
    const { el } = setup({ capacityError: 'Failed to load dispatch capacity' });
    expect(el.textContent).toContain('Capacity unavailable');
  });

  it('loads capacity when it is created', () => {
    const { load } = setup();
    expect(load).toHaveBeenCalled();
  });

  it('shows the waiting count', () => {
    const { el } = setup({ waiting: 7 });
    expect(el.querySelector('.board-summary__waiting')?.textContent).toBe('7 waiting');
  });

  it('offers a button to the cards that need you', () => {
    const { el } = setup({ needs: 2 });
    expect(el.querySelector('button.board-summary__needs')?.textContent?.trim()).toBe('2 need you');
  });

  it('reads one card that needs you in the singular', () => {
    const { el } = setup({ needs: 1 });
    expect(el.querySelector('button.board-summary__needs')?.textContent?.trim()).toBe('1 needs you');
  });

  it('emits when the needs-you button is activated', () => {
    // Arrange
    const { fixture, el } = setup({ needs: 2 });
    let emitted = 0;
    fixture.componentInstance.needsYou.subscribe(() => emitted++);

    // Act
    (el.querySelector('button.board-summary__needs') as HTMLButtonElement).click();

    // Assert
    expect(emitted).toBe(1);
  });

  it('says so in plain text when nothing needs you', () => {
    // Act
    const { el } = setup({ needs: 0 });

    // Assert
    expect(el.querySelector('button.board-summary__needs')).toBeNull();
    expect(el.textContent).toContain('Nothing needs you');
  });

  it('hosts the dispatch controls and the connection indicator', () => {
    const { el } = setup();
    expect(el.querySelector('fd-dispatch-controls')).not.toBeNull();
    expect(el.querySelector('fd-connection-indicator')).not.toBeNull();
  });

  it('links to the resolved history', () => {
    const { el } = setup();
    const link = el.querySelector('a.board-summary__history')!;
    expect(link.getAttribute('href')).toBe('/issues');
    expect(link.textContent).toBe('Resolved history');
  });
});
