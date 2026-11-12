import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';
import { SecretFinding } from '../../workers/worker-run.model';

/**
 * The diff gate's findings that blocked a push: path, line and rule only. The matched value is
 * never sent to the browser, and every field renders by text interpolation.
 */
@Component({
  selector: 'fd-secret-findings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="secret-findings">
      <span class="secret-findings__key" [id]="headingId()">Findings</span>
      <ul class="secret-findings__list" role="list" [attr.aria-labelledby]="headingId()">
        @for (finding of findings(); track $index) {
          <li class="secret-findings__row">
            <code class="secret-findings__path">{{ finding.path }}</code>
            <span class="secret-findings__meta">
              <span class="secret-findings__line">line {{ finding.line }}</span>
              <span class="secret-findings__separator" aria-hidden="true">·</span>
              <span class="secret-findings__rule">Rule <code>{{ finding.ruleId }}</code></span>
            </span>
          </li>
        }
      </ul>
    </div>
  `,
  styleUrl: './secret-findings.scss',
})
export class SecretFindingsComponent {
  readonly findings: InputSignal<readonly SecretFinding[]> = input.required<readonly SecretFinding[]>();
  readonly headingId: InputSignal<string> = input.required<string>();
}
