import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';
import { DispatchCapacityService } from '../../../core/services/dispatch-capacity.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { CodeStep, IssueSummary } from '../../issues/issue.model';
import { IssueService } from '../../issues/issue.service';
import { ItemView } from '../board-state.service';
import { StepItem } from '../board.model';
import { ProgressBandComponent } from './progress-band';

function buildIssue(issueNumber: number, codeStep: CodeStep): IssueSummary {
  return {
    id: `issue-${issueNumber}`,
    issueNumber,
    title: 'A title long enough to fill the title column of the row',
    state: 'merge_pending',
    // A long slug, so the repository column is as wide as it gets.
    repositorySlug: 'acme/a-repository-name-long-enough-to-fill-its-column',
    detectedAt: '2026-01-01T00:00:00Z',
    url: `https://github.com/acme/api/issues/${issueNumber}`,
    repositoryEligibilityStatus: 'eligible',
    stage: 'merge',
    needsAction: false,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    codeStep,
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
  };
}

function step(issueNumber: number, codeStep: CodeStep): ItemView<StepItem> {
  return {
    band: 'steps',
    issue: buildIssue(issueNumber, codeStep),
    stage: 'merge',
    step: codeStep,
    phase: 'live',
    arrived: false,
    moved: false,
  };
}

@Component({
  selector: 'fd-progress-band-steps-layout-host',
  imports: [ProgressBandComponent],
  template: `<div style="width: 1100px">
    <fd-progress-band [workers]="[]" [steps]="steps" [queuedCount]="0" [now]="0" />
  </div>`,
})
class HostComponent {
  readonly steps: readonly ItemView<StepItem>[] = [step(1, 'catch_up_waiting'), step(2, 'merging')];
}

function render(): HTMLElement {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: WorkerSignalRService,
        useValue: { activeAgentCount: signal(0), agentActivityForIssue: () => null },
      },
      {
        provide: DispatchCapacityService,
        useValue: {
          capacity: signal(null),
          loadError: signal(null),
          load: () => Promise.resolve('loaded'),
        },
      },
      { provide: IssueService, useValue: { liveIssueCount: signal(2) } },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('progress band step rows layout', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => page.viewport(original.width, original.height));

  it('fits the waiting chip label inside the chip on a wide row', async () => {
    // Arrange: a wide viewport, so the row lays out in its columns rather than the compact form.
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert: the chip clips with overflow hidden and no ellipsis, so overflow would cut the label.
    const chip = el.querySelector<HTMLElement>('.step-chip--waiting')!;
    expect(chip.textContent?.trim()).toBe('Waiting to catch up');
    expect(chip.clientWidth).toBeGreaterThan(0);
    expect(chip.scrollWidth).toBeLessThanOrEqual(chip.clientWidth);
  });

  it('keeps the waiting chip still while the working chip animates', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert
    const waiting = el.querySelector<HTMLElement>('.step-chip--waiting')!;
    const working = el.querySelector<HTMLElement>('.step-chip:not(.step-chip--waiting)')!;
    expect(getComputedStyle(waiting, '::after').display).toBe('none');
    expect(getComputedStyle(waiting.querySelector('.step-chip__dot')!).animationName).toBe('none');
    expect(getComputedStyle(working, '::after').animationName).not.toBe('none');
    expect(getComputedStyle(working.querySelector('.step-chip__dot')!).animationName).not.toBe(
      'none',
    );
  });
});
