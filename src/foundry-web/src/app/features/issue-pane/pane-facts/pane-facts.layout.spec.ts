import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { IssueDetail } from '../../issues/issue.model';
import { WorkerRunService } from '../../workers/worker-run.service';
import { PaneFactsComponent } from './pane-facts';

// The longest reason triage may write: 200 characters, one line.
const LONG_REASON = 'Changes the outbox relay ordering and the idempotency key of the claim, '
  .repeat(3)
  .slice(0, 200);

const DETAIL: IssueDetail = {
  id: 'abc',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'revision_queued',
  repositorySlug: 'acme/api',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/acme/api/issues/42',
  stage: 'implement',
  needsAction: false,
  awaitsDispatch: true,
  waitsOnBlockers: false,
  runKind: null,
  repositoryId: 'repo-1',
  enteredStateAt: '2026-01-01T00:00:00Z',
  round: 1,
  author: 'dev',
  labels: [],
  stateDetails: null,
  kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
  triage: { outcome: 'ready', kind: 'feature', questions: [], decidedAt: '2026-01-01T00:00:00Z' },
  implementDemand: 'demanding',
  demandReason: LONG_REASON,
  demandEscalated: true,
  clarifications: [],
};

@Component({
  selector: 'fd-pane-facts-layout-host',
  imports: [PaneFactsComponent],
  template: `<div class="host" style="width: 360px"><fd-pane-facts [detail]="detail" [run]="null" [summary]="null" /></div>`,
})
class HostComponent {
  readonly detail = DETAIL;
}

describe('pane facts layout with an escalated demand', () => {
  it('keeps both chips and a 200-character reason inside the Demand value at 360px', async () => {
    // Arrange
    TestBed.configureTestingModule({
      providers: [{ provide: WorkerRunService, useValue: { getLog: () => of(null) } }],
    });
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
    const dt = Array.from(host.querySelectorAll('dt')).find((d) => d.textContent?.trim() === 'Demand')!;
    const dd = dt.nextElementSibling as HTMLElement;
    const box = dd.getBoundingClientRect();

    // Act
    const parts = Array.from(dd.querySelectorAll<HTMLElement>('.pane-facts__chip, .pane-facts__note, .pane-facts__reason'));
    const outside = parts.filter((p) => {
      const r = p.getBoundingClientRect();
      return r.left < box.left - 0.5 || r.right > box.right + 0.5 || r.top < box.top - 0.5 || r.bottom > box.bottom + 0.5;
    });

    // Assert
    expect(LONG_REASON).toHaveLength(200);
    expect(Array.from(dd.querySelectorAll('.pane-facts__chip')).map((c) => c.textContent?.trim())).toEqual([
      'Demanding',
      'Escalated',
    ]);
    expect(parts.length).toBeGreaterThanOrEqual(4);
    expect(outside.map((p) => p.className)).toEqual([]);
    expect(host.scrollWidth).toBe(host.clientWidth);
    expect(dd.querySelector('.pane-facts__reason')!.getBoundingClientRect().height).toBeGreaterThan(40);
  });
});
