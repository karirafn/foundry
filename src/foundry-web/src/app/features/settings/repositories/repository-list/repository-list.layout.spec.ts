import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RepositorySummary } from '../repository.model';
import { RepositoryListComponent } from './repository-list';

const BASE: RepositorySummary = {
  id: 'repo-0',
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
  webhookStatus: 'not-configured',
};

const REPOSITORIES: RepositorySummary[] = [
  {
    ...BASE,
    id: 'repo-1',
    slug: 'acme/platform/payments/reconciliation/billing-reconciliation-service-with-a-long-name',
    providerType: 'gitlab',
    position: 0,
    maxConcurrentWorkers: 3,
    mergeWhenChecksPass: true,
    rebaseAfterRun: false,
    webhookStatus: 'not-registered',
    eligibility: {
      status: 'ineligible',
      violations: [
        {
          rule: 'branch-protection:direct-pushes',
          description: 'The default branch allows direct pushes, so a worker could bypass the merge request review entirely.',
        },
        {
          rule: 'branch-protection:force-pushes',
          description: 'The default branch allows force pushes, which can rewrite history that Foundry has already merged.',
        },
      ],
      reason: null,
    },
  },
  {
    ...BASE,
    id: 'repo-2',
    slug: 'octo-org/tools',
    position: 1,
    eligibility: {
      status: 'ineligible',
      violations: [
        {
          rule: 'package-source-unreachable:https://pkgs.example.com/acme/_packaging/internal-feed/nuget/v3/index.json',
          description: "Foundry can't read the package source https://pkgs.example.com/acme/_packaging/internal-feed/nuget/v3/index.json.",
        },
      ],
      reason: null,
    },
  },
  {
    ...BASE,
    id: 'repo-3',
    slug: 'acme/web',
    position: 2,
    eligibility: { status: 'unreachable', violations: [], reason: 'rate-limited' },
  },
];

@Component({
  selector: 'fd-repository-list-layout-host',
  imports: [RepositoryListComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-repository-list [repositories]="repositories" [reordering]="reordering()" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly reordering = input.required<boolean>();
  readonly repositories = REPOSITORIES;
}

async function render(width: number, reordering: boolean): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  fixture.componentRef.setInput('reordering', reordering);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('repository list layout', () => {
  // 332px is the list width inside a 390px viewport after the settings and section padding.
  it.each([
    [390, false],
    [390, true],
    [332, false],
    [332, true],
  ])('has no horizontal overflow at %ipx (reordering %s)', async (width, reordering) => {
    // Arrange
    const host = await render(width, reordering);
    const hostRight = host.getBoundingClientRect().right;

    // Act
    const overflowing = Array.from(host.querySelectorAll('*'))
      .filter(el => el.getBoundingClientRect().right > hostRight + 0.5)
      .map(el => el.className);
    const list = host.querySelector('.repository-list__list') as HTMLElement;

    // Assert
    expect(overflowing).toEqual([]);
    expect(list.scrollWidth).toBeLessThanOrEqual(list.clientWidth);
  });

  it('moves the status tag onto the title line at 390px', async () => {
    // Arrange
    const host = await render(390, false);

    // Act
    const side = getComputedStyle(host.querySelector('.repository-list__status--side')!);
    const inlineTag = host.querySelector('.repository-list__status--inline') as HTMLElement;
    const inline = getComputedStyle(inlineTag);

    // Assert
    expect(side.display).toBe('none');
    // A flex item's inline-flex is blockified to flex, so assert it shows rather than its exact keyword.
    expect(inline.display).not.toBe('none');
    expect(inlineTag.getBoundingClientRect().width).toBeGreaterThan(0);
  });
});
