import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  InputSignal,
  OutputEmitterRef,
  ViewChild,
  computed,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import {
  BranchCreatedContent,
  FinalReportContent,
  MilestoneContent,
  WorkerReportSummary,
} from '../worker-report.model';

@Component({
  selector: 'fd-worker-log-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SafeHrefPipe],
  templateUrl: './worker-log-panel.html',
  styleUrl: './worker-log-panel.scss',
})
export class WorkerLogPanelComponent {
  readonly reports: InputSignal<WorkerReportSummary[]> = input.required<WorkerReportSummary[]>();
  readonly loading: InputSignal<boolean> = input.required<boolean>();
  readonly error: InputSignal<string | null> = input.required<string | null>();
  readonly isLive: InputSignal<boolean> = input.required<boolean>();
  readonly hideHeader: InputSignal<boolean> = input<boolean>(false);
  readonly issueUrl = input<string | null>(null);
  readonly retry: OutputEmitterRef<void> = output<void>();

  @ViewChild('scrollContainer') private readonly _scrollContainer?: ElementRef<HTMLElement>;

  protected readonly _userScrolledUp = signal(false);

  readonly showEmpty = computed<boolean>(
    () => !this.loading() && !this.error() && this.reports().length === 0
  );

  constructor() {
    effect(() => {
      const reports = this.reports();
      const isLive = this.isLive();
      if (reports.length > 0 && isLive && !this._userScrolledUp()) {
        this._scrollToBottom();
      }
    });
  }

  private static readonly _KNOWN_STATUSES = new Set(['success', 'failed', 'failure', 'partial']);

  formatTimestamp(isoString: string): string {
    const date = new Date(isoString);
    const hh = date.getUTCHours().toString().padStart(2, '0');
    const mm = date.getUTCMinutes().toString().padStart(2, '0');
    const ss = date.getUTCSeconds().toString().padStart(2, '0');
    return `[${hh}:${mm}:${ss}]`;
  }

  parseFinalContent(content: string): FinalReportContent | null {
    try {
      const parsed = JSON.parse(content) as FinalReportContent;
      return parsed;
    } catch {
      return null;
    }
  }

  parseBranchCreatedContent(report: WorkerReportSummary): BranchCreatedContent | null {
    try {
      const parsed = JSON.parse(report.content) as BranchCreatedContent;
      return parsed;
    } catch {
      return null;
    }
  }

  parseMilestoneContent(report: WorkerReportSummary): MilestoneContent | null {
    try {
      const parsed = JSON.parse(report.content) as MilestoneContent;
      return parsed;
    } catch {
      return null;
    }
  }

  buildBranchUrl(branchName: string): string | null {
    const url = this.issueUrl();
    if (!url) {
      return null;
    }
    if (url.includes('/-/issues/')) {
      return url.replace(/\/-\/issues\/\d+/, `/-/tree/${branchName}`);
    }
    if (url.includes('/issues/')) {
      return url.replace(/\/issues\/\d+/, `/tree/${branchName}`);
    }
    return null;
  }

  safeStatus(status: string): string {
    return WorkerLogPanelComponent._KNOWN_STATUSES.has(status) ? status : 'unknown';
  }

  onRetry(): void {
    this.retry.emit();
  }

  onScrollToBottom(): void {
    this._userScrolledUp.set(false);
    this._scrollToBottom();
  }

  onScroll(event: Event): void {
    const el = event.target as HTMLElement;
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
    this._userScrolledUp.set(!atBottom);
  }

  private _scrollToBottom(): void {
    const container = this._scrollContainer?.nativeElement;
    if (container) {
      container.scrollTop = container.scrollHeight;
    }
  }
}
