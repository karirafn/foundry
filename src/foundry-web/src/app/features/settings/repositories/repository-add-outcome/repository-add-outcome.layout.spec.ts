import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CreateRepositoryResponse } from '../repository.model';
import { RepositoryAddOutcomeComponent } from './repository-add-outcome';

const RESPONSE: CreateRepositoryResponse = {
  repository: {
    id: 'repo-1',
    slug: 'acme/web',
    accountId: 'account-1',
    accountName: 'acme',
    providerType: 'github',
    position: 0,
    pollIntervalSeconds: null,
    effectivePollIntervalSeconds: 300,
    pollIntervalIsDefault: true,
    isActive: true,
    maxConcurrentWorkers: 1,
    rebaseAfterRun: true,
    mergeWhenChecksPass: false,
    reviewPullRequests: true,
    lastPolledAt: null,
    eligibility: { status: 'eligible', violations: [], reason: null },
  },
  branchRules: { kind: 'applied' },
  webhook: {
    kind: 'failed',
    reason: 'provider-error',
    detail: 'Validation-Failed:https://foundry.example.com/api/webhooks/github/0f8fad5b-d9cb-469f-a165-70867728950e-is-not-reachable-from-the-provider',
  },
};

@Component({
  selector: 'fd-repository-add-outcome-layout-host',
  imports: [RepositoryAddOutcomeComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-repository-add-outcome [response]="response" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly response = RESPONSE;
}

async function render(width: number): Promise<HTMLElement> {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('repository add outcome layout', () => {
  it.each([320, 720])('has no horizontal overflow at %ipx with a long webhook detail', async (width) => {
    // Arrange
    const host = await render(width);
    const hostRight = host.getBoundingClientRect().right;

    // Act
    const overflowing = Array.from(host.querySelectorAll('*'))
      .filter(el => el.getBoundingClientRect().right > hostRight + 0.5)
      .map(el => el.className);

    // Assert
    expect(overflowing).toEqual([]);
  });

  it('keeps the webhook label and status on one line at 320px', async () => {
    // Arrange
    const host = await render(320);
    const row = host.querySelector('[data-row="webhook"]') as HTMLElement;

    // Act
    const label = row.querySelector('.repository-add-outcome__label')!.getBoundingClientRect();
    const status = row.querySelector('.repository-add-outcome__status')!.getBoundingClientRect();

    // Assert
    expect(Math.abs(label.top - status.top)).toBeLessThan(4);
    expect(status.height).toBeLessThan(label.height * 1.5);
  });

  it('places the repository link alone on the last line of the details', async () => {
    // Arrange
    const host = await render(320);
    const details = host.querySelector('[data-row="webhook"] .repository-add-outcome__details') as HTMLElement;

    // Act
    const link = details.querySelector('.repository-add-outcome__repository-link')!.getBoundingClientRect();
    const detail = details.querySelector('.repository-add-outcome__detail')!.getBoundingClientRect();

    // Assert
    expect(link.top).toBeGreaterThanOrEqual(detail.bottom);
    expect(details.lastElementChild?.classList).toContain('repository-add-outcome__repository-link');
  });
});
