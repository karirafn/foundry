import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  NgZone,
  OnInit,
  WritableSignal,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable } from 'rxjs';

let nextId = 0;

const ANNOUNCEMENT_DEBOUNCE_MS = 3000;

@Component({
  selector: 'fd-log-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  template: `
    <div class="log-view">
      <button
        class="log-view__toggle"
        type="button"
        [attr.aria-expanded]="_expanded().toString()"
        [attr.aria-controls]="_bodyId"
        (click)="_toggle()"
      >
        <span class="log-view__toggle-icon" aria-hidden="true">{{ _expanded() ? '▼' : '▶' }}</span>
        <span class="log-view__toggle-label">{{ label() }}</span>
      </button>

      <!-- Body is always mounted so aria-controls resolves and the role="log" live region pre-exists before content arrives -->
      <div
        class="log-view__body"
        [id]="_bodyId"
        [hidden]="!_expanded()"
        role="log"
        aria-label="Worker log output"
      >
        @if (_displayLines().length > 0) {
          <pre class="log-view__pre"><code>@for (line of _displayLines(); track $index) {
<span class="log-view__line">{{ line }}</span>
}</code></pre>
        } @else {
          <div class="log-view__empty" aria-label="No log output">No log output available.</div>
        }
      </div>

      <!-- Persistent aria-live region: always mounted to avoid suppression of announcements -->
      <div
        class="sr-only"
        aria-live="polite"
        aria-atomic="true"
      >{{ _liveAnnouncement() }}</div>
    </div>
  `,
  styleUrl: './log-view.scss',
})
export class LogViewComponent implements OnInit {
  readonly mode = input.required<'stream' | 'static'>();
  readonly lines = input<string[] | null>(null);
  readonly logStream = input<Observable<string> | null>(null);
  readonly label = input<string>('Log');
  readonly expanded = input<boolean>(true);

  protected readonly _bodyId = `log-view-body-${nextId++}`;

  protected readonly _expanded: WritableSignal<boolean> = signal(true);
  protected readonly _streamedLines: WritableSignal<string[]> = signal([]);
  protected readonly _liveAnnouncement: WritableSignal<string> = signal('');

  protected readonly _displayLines = computed(() => {
    if (this.mode() === 'stream') {
      return this._streamedLines();
    }
    return this.lines() ?? [];
  });

  private readonly _destroyRef = inject(DestroyRef);
  private readonly _ngZone = inject(NgZone);

  private _pendingLineCount = 0;
  private _announcementTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this._expanded.set(this.expanded());

    if (this.mode() === 'static') {
      const staticLines = this.lines();
      if (staticLines !== null && staticLines.length > 0) {
        this._liveAnnouncement.set(`Log loaded, ${staticLines.length} line${staticLines.length === 1 ? '' : 's'}`);
      }
      return;
    }

    const stream = this.logStream();
    if (stream !== null) {
      stream.pipe(takeUntilDestroyed(this._destroyRef)).subscribe((line) => {
        this._streamedLines.update((prev) => [...prev, line]);
        this._scheduleBatchAnnouncement();
      });
    }
  }

  protected _toggle(): void {
    this._expanded.update((v) => !v);
  }

  private _scheduleBatchAnnouncement(): void {
    this._pendingLineCount++;

    if (this._announcementTimer !== null) {
      return;
    }

    this._announcementTimer = this._ngZone.runOutsideAngular(() =>
      setTimeout(() => {
        this._ngZone.run(() => {
          const count = this._pendingLineCount;
          this._pendingLineCount = 0;
          this._announcementTimer = null;
          this._liveAnnouncement.set(`${count} new log line${count === 1 ? '' : 's'}`);
        });
      }, ANNOUNCEMENT_DEBOUNCE_MS)
    );

    this._destroyRef.onDestroy(() => {
      if (this._announcementTimer !== null) {
        clearTimeout(this._announcementTimer);
        this._announcementTimer = null;
      }
    });
  }
}
