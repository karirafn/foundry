import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { page } from 'vitest/browser';
import { WorkerSignalRService } from '../../../../core/services/worker-signalr.service';
import { IssueSummary } from '../../../issues/issue.model';
import { IssueService } from '../../../issues/issue.service';
import { ItemView } from '../../board-state.service';
import { NeedsItem, buildBoardBands } from '../../board.model';
import { LifecycleRowComponent } from '../lifecycle-row';
import { RowActionComponent } from './row-action';

function failedItem(classification: string): ItemView<NeedsItem> {
  const issue: IssueSummary = {
    id: 'issue-873',
    issueNumber: 873,
    title: 'GraphQL reads record their rate-limit headroom from data.rateLimit',
    state: 'failed',
    repositorySlug: 'acme/foundry',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/foundry/issues/873',
    failureClassification: classification,
    stage: 'implement',
    needsAction: true,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
    lifecycle: [
      { step: 'implement', actor: 'claude', status: 'current', phase: 'failed' },
      { step: 'publish', actor: 'code', status: 'upcoming', phase: null },
    ],
  };
  return { ...buildBoardBands([issue], [], []).needs[0], phase: 'live', arrived: false, moved: false };
}

@Component({
  selector: 'fd-row-action-layout-host',
  imports: [LifecycleRowComponent, RowActionComponent],
  // The board page's own padding, so the row is as wide as it is on the page.
  template: `<div style="padding: 24px">
    <fd-lifecycle-row [item]="item()" [now]="0">
      <fd-row-action rowAction [item]="item()" />
    </fd-lifecycle-row>
  </div>`,
})
class HostComponent {
  readonly item = signal(failedItem('timeout'));
}

function render(classification = 'timeout', retryError: string | null = null): HTMLElement {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: WorkerSignalRService,
        useValue: { activeAgentCount: signal(0), agentActivityForIssue: () => null },
      },
      { provide: IssueService, useValue: { retryIssue: () => of(retryError) } },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.item.set(failedItem(classification));
  fixture.detectChanges();
  if (retryError !== null) {
    fixture.nativeElement.querySelector('.row-action__btn').click();
    fixture.detectChanges();
  }
  return fixture.nativeElement as HTMLElement;
}

function box(el: HTMLElement, selector: string): DOMRect {
  return el.querySelector<HTMLElement>(selector)!.getBoundingClientRect();
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('row action layout', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => page.viewport(original.width, original.height));

  it.each([760, 1280])('sits at the end of the status line at %ipx, above the title overlay', async (width) => {
    // Arrange
    await page.viewport(width, 800);

    // Act
    const el = render();

    // Assert
    const status = box(el, '.lifecycle-row__status');
    const button = box(el, '.row-action__btn');
    const clock = box(el, '.lifecycle-row__clock');
    expect(button.height).toBeGreaterThanOrEqual(32);
    expect(Math.abs(button.right - status.right)).toBeLessThanOrEqual(1);
    expect(button.top).toBeLessThan(clock.bottom);
    expect(button.left).toBeGreaterThan(clock.right);
    const hit = document.elementFromPoint(button.left + button.width / 2, button.top + button.height / 2);
    expect(hit?.closest('.row-action__btn')).not.toBeNull();
    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(width);
  });

  it.each([320, 759])('takes its own full-width line under the status text at %ipx', async (width) => {
    // Arrange
    await page.viewport(width, 800);

    // Act
    const el = render('a_failure_category_name_long_enough_to_wrap_the_explanation_onto_several_lines');

    // Assert
    const status = box(el, '.lifecycle-row__status');
    const button = box(el, '.row-action__btn');
    const why = box(el, '.lifecycle-row__why');
    const clock = box(el, '.lifecycle-row__clock');
    expect(button.top).toBeGreaterThanOrEqual(Math.max(why.bottom, clock.bottom));
    expect(Math.abs(button.left - status.left)).toBeLessThanOrEqual(1);
    expect(Math.abs(button.right - status.right)).toBeLessThanOrEqual(1);
    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(width);
  });

  it("puts the action's error on its own line under the status", async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render('timeout', 'Failed to retry issue.');

    // Assert
    const status = box(el, '.lifecycle-row__status');
    const button = box(el, '.row-action__btn');
    const error = box(el, '.row-action__error');
    expect(error.top).toBeGreaterThanOrEqual(button.bottom);
    expect(Math.abs(error.left - status.left)).toBeLessThanOrEqual(1);
  });
});
