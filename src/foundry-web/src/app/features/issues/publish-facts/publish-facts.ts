import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { PublishFacts } from '../../workers/worker-run.model';
import { ProviderTerminology, providerTerminology } from '../../../shared/utils/provider.util';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';

const SHORT_SHA_LENGTH = 7;

/** What Foundry published for a run: head commit, commits on the branch and the pull request. */
@Component({
  selector: 'fd-publish-facts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SafeHrefPipe],
  template: `
    <div class="publish-facts">
      <span class="publish-facts__key">Published</span>
      <div class="publish-facts__fields">
        <div class="publish-facts__field">
          <span class="publish-facts__key">Head commit</span>
          @if (facts().headCommitUrl | safeHref; as href) {
            <a
              class="publish-facts__sha publish-facts__link"
              [href]="href"
              target="_blank"
              rel="noopener noreferrer"
              [attr.title]="facts().headSha"
              [attr.aria-label]="'Head commit ' + facts().headSha"
            >{{ shortSha() }}</a>
          } @else {
            <span
              class="publish-facts__sha"
              [attr.title]="facts().headSha"
              [attr.aria-label]="'Head commit ' + facts().headSha"
            >{{ shortSha() }}</span>
          }
        </div>
        <div class="publish-facts__field">
          <span class="publish-facts__key">Commits on branch</span>
          <span class="publish-facts__value publish-facts__commit-count">{{ commitCountText() }}</span>
        </div>
        @if (facts().pullRequestUrl | safeHref; as prHref) {
          <div class="publish-facts__field">
            <span class="publish-facts__key">{{ terminology().pullRequest }}</span>
            <a
              class="publish-facts__value publish-facts__link publish-facts__pr-link"
              [href]="prHref"
              target="_blank"
              rel="noopener noreferrer"
              [attr.aria-label]="prLinkLabel()"
            >View {{ terminology().prAbbrev }}</a>
          </div>
        }
      </div>
    </div>
  `,
  styleUrl: './publish-facts.scss',
})
export class PublishFactsComponent {
  readonly facts: InputSignal<PublishFacts> = input.required<PublishFacts>();
  readonly branchName: InputSignal<string | null> = input<string | null>(null);
  readonly providerType: InputSignal<string> = input<string>('');

  protected readonly shortSha: Signal<string> = computed(() => this.facts().headSha.slice(0, SHORT_SHA_LENGTH));

  protected readonly commitCountText: Signal<string> = computed(() => {
    const count = this.facts().branchCommitCount;
    if (count === 0) {
      return 'No commits';
    }
    return count === 1 ? '1 commit' : `${count} commits`;
  });

  protected readonly terminology: Signal<ProviderTerminology> = computed(() => providerTerminology(this.providerType()));

  protected readonly prLinkLabel: Signal<string> = computed(() => {
    const noun = this.terminology().pullRequest.toLowerCase();
    const branch = this.branchName();
    return branch !== null ? `View ${noun} for branch ${branch}` : `View ${noun}`;
  });
}
