import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { IssueDetail } from '../../issues/issue.model';
import { IssueService } from '../../issues/issue.service';
import { WorkerRunService } from '../../workers/worker-run.service';
import { PaneAction } from '../pane-callout.model';
import { PaneActionsComponent } from './pane-actions';

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
  stateDetails: null,
  kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
  triage: null,
  clarifications: [],
};

@Component({
  selector: 'fd-pane-actions-layout-host',
  imports: [PaneActionsComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-pane-actions [detail]="detail" [run]="null" [actions]="actions" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly detail = DETAIL;
  readonly actions: PaneAction[] = ['retry_post', 'continue_without_answer'];
}

async function render(width: number): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: IssueService,
        useValue: {},
      },
      { provide: WorkerRunService, useValue: { requestCancel: () => of(null) } },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('pane actions layout', () => {
  it.each([240, 332, 720])('keeps Retry post and Continue without answer inside the pane at %ipx', async (width) => {
    // Arrange
    const host = await render(width);
    const hostRight = host.getBoundingClientRect().right;

    // Act
    const buttons = Array.from(host.querySelectorAll<HTMLButtonElement>('.pane-actions button'));
    const overflowing = buttons.filter((b) => b.getBoundingClientRect().right > hostRight + 0.5);

    // Assert
    expect(buttons.map((b) => b.textContent?.trim())).toEqual(['Retry post', 'Continue without answer']);
    expect(overflowing).toEqual([]);
    expect(buttons.every((b) => b.getBoundingClientRect().height >= 32)).toBe(true);
  });
});
