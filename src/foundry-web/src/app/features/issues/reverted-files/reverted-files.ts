import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RevertedFile, RevertedFileReason } from '../../workers/worker-run.model';
import { formatFileSize } from '../../../shared/utils/format-file-size';

/** Rows shown before the rest move behind a "more files" disclosure. */
const VISIBLE_FILE_LIMIT = 10;

interface ReasonDisplay {
  readonly label: string;
  /** Lowercase phrase for the row's screen-reader tail. */
  readonly spoken: string;
  readonly cssClass: string;
}

const REASON_DISPLAY: Readonly<Record<RevertedFileReason, ReasonDisplay>> = {
  oversized: { label: 'Too large', spoken: 'too large', cssClass: 'badge--reverted-oversized' },
  binary: { label: 'Binary', spoken: 'binary', cssClass: 'badge--reverted-binary' },
};

interface RevertedFileRow {
  readonly path: string;
  readonly reason: ReasonDisplay;
  readonly size: string;
}

let nextId = 0;

/** Files the diff gate reverted in a separate commit before pushing, shown in the run block. */
@Component({
  selector: 'fd-reverted-files',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-template #fileRow let-row>
      <li class="reverted-files__row">
        <code class="reverted-files__path">{{ row.path }}</code>
        <span class="badge reverted-files__chip" [class]="row.reason.cssClass" aria-hidden="true">{{ row.reason.label }}</span>
        <span class="reverted-files__size" aria-hidden="true">{{ row.size }}</span>
        <span class="sr-only">, {{ row.reason.spoken }}, {{ row.size }}</span>
      </li>
    </ng-template>

    <div class="reverted-files">
      <span class="reverted-files__key" [id]="headingId">Reverted files</span>
      <p class="reverted-files__note">
        The diff gate reverted these files in a separate commit before pushing.
        The pull request body lists them too.
      </p>
      <ul class="reverted-files__list" role="list" [attr.aria-labelledby]="headingId">
        @for (row of visibleRows(); track $index) {
          <ng-container *ngTemplateOutlet="fileRow; context: { $implicit: row }" />
        }
      </ul>
      @if (overflowRows().length > 0) {
        <details class="reverted-files__overflow">
          <summary>{{ overflowSummary() }}</summary>
          <ul class="reverted-files__list" role="list" [attr.aria-labelledby]="headingId">
            @for (row of overflowRows(); track $index) {
              <ng-container *ngTemplateOutlet="fileRow; context: { $implicit: row }" />
            }
          </ul>
        </details>
      }
    </div>
  `,
  styleUrl: './reverted-files.scss',
  imports: [NgTemplateOutlet],
})
export class RevertedFilesComponent {
  readonly files: InputSignal<readonly RevertedFile[]> = input.required<readonly RevertedFile[]>();

  protected readonly headingId: string = `reverted-files-heading-${nextId++}`;

  private readonly _rows: Signal<readonly RevertedFileRow[]> = computed(() =>
    this.files().map((file) => ({
      path: file.path,
      // An unknown future reason falls back to the binary styling rather than breaking the row.
      reason: REASON_DISPLAY[file.reason] ?? { label: file.reason, spoken: file.reason, cssClass: 'badge--reverted-binary' },
      size: formatFileSize(file.sizeBytes),
    })));

  protected readonly visibleRows: Signal<readonly RevertedFileRow[]> = computed(() =>
    this._rows().slice(0, VISIBLE_FILE_LIMIT));

  protected readonly overflowRows: Signal<readonly RevertedFileRow[]> = computed(() =>
    this._rows().slice(VISIBLE_FILE_LIMIT));

  protected readonly overflowSummary: Signal<string> = computed(() => {
    const count = this.overflowRows().length;
    return count === 1 ? '1 more file' : `${count} more files`;
  });
}
