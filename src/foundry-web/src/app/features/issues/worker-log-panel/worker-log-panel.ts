import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  InputSignal,
  OutputEmitterRef,
  Signal,
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
  readonly containerOutput = input<string | null>(null);
  readonly retry: OutputEmitterRef<void> = output<void>();

  @ViewChild('scrollContainer') private readonly _scrollContainer?: ElementRef<HTMLElement>;

  protected readonly _userScrolledUp = signal(false);
  protected readonly _containerOutputExpanded = signal<boolean>(false);

  private static _nextId = 0;
  protected readonly panelId = `container-output-${WorkerLogPanelComponent._nextId++}`;

  private _initialExpandSet = false;

  readonly showEmpty = computed<boolean>(
    () => !this.loading() && !this.error() && this.reports().length === 0
  );

  readonly isContainerOutputExpanded: Signal<boolean> = computed(
    () => this._containerOutputExpanded()
  );

  constructor() {
    effect(() => {
      const reports = this.reports();
      const isLive = this.isLive();
      if (reports.length > 0 && isLive && !this._userScrolledUp()) {
        this._scrollToBottom();
      }
    });

    effect(() => {
      const output = this.containerOutput();
      if (output && !this._initialExpandSet) {
        this._initialExpandSet = true;
        this._containerOutputExpanded.set(this.reports().length === 0);
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

  parseBranchCreatedContent(content: string): BranchCreatedContent | null {
    try {
      const parsed = JSON.parse(content);
      if (!parsed || typeof parsed.branchName !== 'string' || !parsed.branchName) {
        return null;
      }
      return parsed as BranchCreatedContent;
    } catch {
      return null;
    }
  }

  parseMilestoneContent(content: string): MilestoneContent | null {
    try {
      const parsed = JSON.parse(content);
      if (!parsed || typeof parsed.summary !== 'string' || !parsed.summary) {
        return null;
      }
      return parsed as MilestoneContent;
    } catch {
      return null;
    }
  }

  buildBranchUrl(branchName: string): string | null {
    const url = this.issueUrl();
    if (!url) {
      return null;
    }

    let replaced: string;
    if (url.includes('/-/issues/')) {
      replaced = url.replace(/\/-\/issues\/\d+.*/, `/-/tree/${branchName}`);
    } else if (url.includes('/issues/')) {
      replaced = url.replace(/\/issues\/\d+.*/, `/tree/${branchName}`);
    } else {
      return null;
    }

    return replaced !== url ? replaced : null;
  }

  safeStatus(status: string): string {
    return WorkerLogPanelComponent._KNOWN_STATUSES.has(status) ? status : 'unknown';
  }

  onRetry(): void {
    this.retry.emit();
  }

  toggleContainerOutput(): void {
    this._containerOutputExpanded.set(!this.isContainerOutputExpanded());
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
