import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { WorkerRoleImage } from '../worker-role.service';

const SHORT_COMMIT_LENGTH = 7;

/** The worker image the role catalog was read from: tag, build time and commit, plus a warning when unverified. */
@Component({
  selector: 'fd-role-image',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  template: `
    <dl class="role-image__meta">
      <div class="role-image__meta-item">
        <dt>Image</dt>
        <dd>
          <code class="role-image__tag">{{ image().tag }}</code>
        </dd>
      </div>
      <div class="role-image__meta-item">
        <dt>Built</dt>
        <dd>
          <time class="role-image__built-at" [attr.datetime]="image().builtAt">{{
            image().builtAt | date: 'medium'
          }}</time>
        </dd>
      </div>
      <div class="role-image__meta-item">
        <dt>Commit</dt>
        <dd>
          <code class="role-image__commit" [attr.title]="image().commit">{{ shortCommit() }}</code>
        </dd>
      </div>
    </dl>
    @if (!image().verified) {
      <p class="role-image__unverified" role="note">
        <span class="role-image__unverified-icon" aria-hidden="true">⚠</span>
        <span
          ><strong class="role-image__unverified-lead">Unverified:</strong> image building is
          disabled, so this catalog was read from disk and may differ from the roles in the
          operator-supplied image <code>{{ image().tag }}</code
          >.</span
        >
      </p>
    }
  `,
  styleUrl: './role-image.scss',
})
export class RoleImageComponent {
  readonly image: InputSignal<WorkerRoleImage> = input.required<WorkerRoleImage>();

  protected readonly shortCommit: Signal<string> = computed(() => {
    const commit = this.image().commit;
    return commit ? commit.slice(0, SHORT_COMMIT_LENGTH) : 'unknown';
  });
}
