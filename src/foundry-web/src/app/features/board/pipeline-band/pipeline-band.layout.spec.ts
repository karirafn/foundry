import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';
import { IssueSummary, RunKind } from '../../issues/issue.model';
import { RUN_KIND_LABELS } from '../../../shared/utils/run-kind';
import { ItemView } from '../board-state.service';
import { PipelineItem } from '../board.model';
import { PipelineBandComponent } from './pipeline-band';

const KINDS = Object.keys(RUN_KIND_LABELS) as RunKind[];

function buildIssue(issueNumber: number, runKind: RunKind): IssueSummary {
  return {
    id: `issue-${issueNumber}`,
    issueNumber,
    title: 'A title long enough to fill the title column of the row',
    state: 'queued',
    // A long slug, so the repository column is as wide as it gets.
    repositorySlug: 'acme/a-repository-name-long-enough-to-fill-its-column',
    detectedAt: '2026-01-01T00:00:00Z',
    url: `https://github.com/acme/api/issues/${issueNumber}`,
    repositoryEligibilityStatus: 'eligible',
    stage: runKind === 'triage' ? 'triage' : 'implement',
    needsAction: false,
    awaitsDispatch: true,
    waitsOnBlockers: false,
    codeStep: null,
    runKind,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
  };
}

function row(index: number, runKind: RunKind): ItemView<PipelineItem> {
  const issue = buildIssue(1000 + index, runKind);
  return {
    band: 'pipeline',
    issue,
    stage: issue.stage === 'triage' ? 'triage' : 'implement',
    runKind,
    slot: { kind: 'position', position: index + 1 },
    phase: 'live',
    arrived: false,
    moved: false,
  };
}

@Component({
  selector: 'fd-pipeline-band-layout-host',
  imports: [PipelineBandComponent],
  template: `<div style="width: 1100px"><fd-pipeline-band [items]="items" /></div>`,
})
class HostComponent {
  readonly items: readonly ItemView<PipelineItem>[] = KINDS.map((kind, i) => row(i, kind));
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('pipeline band layout', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => page.viewport(original.width, original.height));

  it('fits every run-kind tag on one line inside its column, clear of the repository', async () => {
    // Arrange: a wide viewport, so the row lays out in its columns rather than the compact form.
    await page.viewport(1280, 800);
    const fixture = TestBed.createComponent(HostComponent);

    // Act
    fixture.detectChanges();
    await fixture.whenStable();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const rows = [...el.querySelectorAll<HTMLElement>('.board-row')];
    expect(rows).toHaveLength(KINDS.length);
    for (const r of rows) {
      const tag = r.querySelector<HTMLElement>('.run-kind-tag')!;
      const repo = r.querySelector<HTMLElement>('.board-row__repo')!;
      const tagBox = tag.getBoundingClientRect();
      const lineHeight = parseFloat(getComputedStyle(tag).lineHeight);
      expect(tagBox.width).toBeGreaterThan(0);
      expect(tagBox.right).toBeLessThanOrEqual(repo.getBoundingClientRect().left);
      // One line: the tag is no taller than a line plus its 1px borders.
      expect(tagBox.height).toBeLessThanOrEqual(lineHeight + 2);
    }
  });
});
