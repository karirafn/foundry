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
  runKind: null,
  repositoryId: 'repo-1',
  enteredStateAt: '2026-01-01T00:00:00Z',
  round: 1,
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
  implementDemand: null,
  demandReason: null,
  demandEscalated: false,
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

const WIDE_TABLE = [
  `| ${Array.from({ length: 8 }, (_, i) => `Column heading ${i}`).join(' | ')} |`,
  `|${' --- |'.repeat(8)}`,
  `| ${Array.from({ length: 8 }, (_, i) => `value-without-breaks-${i}`).join(' | ')} |`,
].join('\n');

const DECIDED: IssueDetail = {
  ...DETAIL,
  state: 'queued',
  clarifications: [
    {
      questions: [],
      input: null,
      askedAt: '2026-01-01T00:00:00Z',
      answeredAt: null,
      decisions: [
        {
          question: `Which caps? ${LONG_WORD}`,
          decision: `Caps per state:\n${WIDE_TABLE}\n${LONG_WORD}`,
          ground: 'implementation_detail',
          evidence: [LONG_PATH],
        },
      ],
    },
  ],
};

@Component({
  selector: 'fd-pane-clarifications-decided-layout-host',
  imports: [PaneClarificationsComponent],
  template: `<div class="host" style="width: 480px"><fd-pane-clarifications [detail]="detail" providerType="github" /></div>`,
})
class DecidedHostComponent {
  readonly detail = DECIDED;
}

describe('pane clarifications layout with a decided round', () => {
  it('scrolls a wide decision table inside its own region at a 480px pane', async () => {
    // Arrange
    const fixture = TestBed.createComponent(DecidedHostComponent);
    await fixture.whenStable();
    const host = (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
    host.querySelectorAll('details').forEach((d) => (d.open = true));

    // Act
    const wrap = host.querySelector<HTMLElement>('.pane-clarifications__table-wrap')!;

    // Assert
    expect(host.scrollWidth).toBe(host.clientWidth);
    expect(wrap.scrollWidth).toBeGreaterThan(wrap.clientWidth);
    expect(wrap.getBoundingClientRect().right).toBeLessThanOrEqual(host.getBoundingClientRect().right + 0.5);
  });
});

const REQUESTED: IssueDetail = {
  ...DETAIL,
  state: 'revision_queued',
  clarifications: [
    {
      questions: [],
      input: null,
      askedAt: '2026-01-01T00:00:00Z',
      answeredAt: null,
      decisions: [],
      feedback: {
        comments: [
          { body: Array.from({ length: 10 }, (_, i) => `Line ${i} https://example.test/${LONG_WORD}`).join('\n'), path: LONG_PATH, line: 7 },
          { body: `General ${LONG_WORD}`, path: null, line: null },
        ],
        omittedCommentCount: 3,
      },
    },
  ],
};

@Component({
  selector: 'fd-pane-clarifications-requested-layout-host',
  imports: [PaneClarificationsComponent],
  template: `<div class="host" style="width: 480px"><fd-pane-clarifications [detail]="detail" providerType="github" /></div>`,
})
class RequestedHostComponent {
  readonly detail = REQUESTED;
}

describe('pane clarifications layout with a review-feedback round', () => {
  it('wraps a long comment and a long path inside a 480px pane', async () => {
    // Arrange
    const fixture = TestBed.createComponent(RequestedHostComponent);
    await fixture.whenStable();
    const host = (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
    host.querySelector<HTMLButtonElement>('.pane-clarifications__more')!.click();
    await fixture.whenStable();
    const hostRight = host.getBoundingClientRect().right;

    // Act
    const overflowing = Array.from(host.querySelectorAll<HTMLElement>('section *'))
      .filter((b) => b.getBoundingClientRect().right > hostRight + 0.5);

    // Assert
    expect(host.querySelectorAll('ul[aria-label="Review comments"] > li').length).toBe(3);
    expect(host.scrollWidth).toBe(host.clientWidth);
    expect(overflowing.map((b) => b.className || b.tagName)).toEqual([]);
  });
});
