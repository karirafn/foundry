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
import { FinalReportContent, WorkerReportSummary } from '../worker-report.model';

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

  formatTimestamp(isoString: string): string {
    const date = new Date(isoString);
    const hh = date.getHours().toString().padStart(2, '0');
    const mm = date.getMinutes().toString().padStart(2, '0');
    const ss = date.getSeconds().toString().padStart(2, '0');
    return `[${hh}:${mm}:${ss}]`;
  }

  parseFinalContent(content: string): FinalReportContent | null {
    try {
      return JSON.parse(content) as FinalReportContent;
    } catch {
      return null;
    }
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
