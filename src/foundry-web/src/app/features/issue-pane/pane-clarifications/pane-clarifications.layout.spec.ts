import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { InputQuestion, IssueDetail } from '../../issues/issue.model';
import { PaneClarificationsComponent } from './pane-clarifications';

const LONG_WORD = 'x'.repeat(300);
const LONG_PATH = `src/${'a'.repeat(290)}/x.ts:42`;

const STRUCTURED: InputQuestion = {
  $type: 'structured',
  question: `Which API? ${LONG_WORD}`,
  problem: `Two exist. ${LONG_WORD}`,
  impact: 'Clients break.',
  options: ['v1', `v2 ${LONG_WORD}`, 'v3'],
  recommendedOption: 'b',
  recommendationReason: `Clients moved. ${LONG_WORD}`,
  evidence: [LONG_PATH, 'src/api.ts:3 routes v2'],
};

const DETAIL: IssueDetail = {
  id: 'abc',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'awaiting_input',
  repositorySlug: 'acme/api',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/acme/api/issues/42',
  stage: null,
  needsAction: true,
  awaitsDispatch: false,
  waitsOnBlockers: false,
  codeStep: null,
  runKind: null,
  repositoryId: 'repo-1',
  enteredStateAt: '2026-01-01T00:00:00Z',
  author: 'dev',
  labels: [],
  stateDetails: {
    workerRunId: null,
    branchName: null,
    feedbackCutoffAt: null,
    failureReason: null,
    failedAt: null,
    completedAt: null,
    blockedBy: null,
    violations: null,
    transientRetry: null,
    awaitingInput: {
      questions: [],
      postStatus: 'failed',
      postedAt: null,
      commentUrl: null,
      postFailureReason: `Forbidden ${LONG_WORD}`,
      nextPostAttemptAt: null,
      resumeTarget: 'revision',
      reminderSentAt: null,
      reminderDueAt: null,
    },
  },
  kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
  triage: null,
  clarifications: [
    {
      questions: [{ $type: 'legacy', question: `Which API? ${LONG_WORD}`, context: 'Two exist.', recommendation: 'The v2 one.' }, STRUCTURED],
      input: [
        { kind: 'comments', text: Array.from({ length: 20 }, (_, i) => `Line ${i} ${LONG_WORD}`).join('\n') },
        { kind: 'descriptionEdited' },
        {
          kind: 'branchUpdated',
          fromSha: 'aaaaaaa1111111',
          toSha: 'bbbbbbb2222222',
          compareUrl: 'https://github.com/acme/api/compare/aaaaaaa1111111...bbbbbbb2222222',
        },
      ],
      askedAt: '2026-01-01T00:00:00Z',
      answeredAt: '2026-01-02T00:00:00Z',
    },
    {
      questions: [{ $type: 'legacy', question: 'And the schema?', context: '' }, STRUCTURED],
      input: null,
      askedAt: '2026-01-03T00:00:00Z',
      answeredAt: null,
    },
  ],
};

@Component({
  selector: 'fd-pane-clarifications-layout-host',
  imports: [PaneClarificationsComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-pane-clarifications [detail]="detail" providerType="github" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly detail = DETAIL;
}

async function setup(width: number) {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  await fixture.whenStable();
  return { fixture, host: (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement };
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('pane clarifications layout', () => {
  it.each([240, 332, 720])('keeps every box inside the pane with long unbroken text at %ipx', async (width) => {
    // Arrange
    const { host } = await setup(width);
    host.querySelectorAll('details').forEach((d) => (d.open = true));
    const hostRight = host.getBoundingClientRect().right;

    // Act
    const boxes = Array.from(host.querySelectorAll<HTMLElement>('section *'));
    const overflowing = boxes.filter((b) => b.getBoundingClientRect().right > hostRight + 0.5);

    // Assert
    expect(boxes.length).toBeGreaterThan(0);
    expect(overflowing.map((b) => b.className || b.tagName)).toEqual([]);
  });

  it('collapses a long comment to six lines until it is expanded', async () => {
    // Arrange
    const { fixture, host } = await setup(720);
    const comment = host.querySelector<HTMLElement>('.pane-clarifications__comment')!;
    const lineHeight = parseFloat(getComputedStyle(comment).lineHeight);
    const paddings = parseFloat(getComputedStyle(comment).paddingTop) + parseFloat(getComputedStyle(comment).paddingBottom);
    const collapsed = comment.getBoundingClientRect().height;

    // Act
    host.querySelector<HTMLButtonElement>('.pane-clarifications__more')!.click();
    await fixture.whenStable();
    const expanded = comment.getBoundingClientRect().height;

    // Assert
    expect(Math.round(collapsed - paddings)).toBeLessThanOrEqual(Math.round(lineHeight * 6) + 1);
    expect(expanded).toBeGreaterThan(collapsed);
  });

  it('gives every disclosure summary at least a 32px target', async () => {
    // Arrange
    const { host } = await setup(332);

    // Act
    const heights = Array.from(host.querySelectorAll('summary')).map((s) => s.getBoundingClientRect().height);

    // Assert
    expect(heights.length).toBeGreaterThan(0);
    expect(heights.filter((h) => h < 32)).toEqual([]);
  });
});
