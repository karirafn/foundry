import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
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

      @if (_expanded()) {
        <div class="log-view__body" [id]="_bodyId" role="log" aria-label="Worker log output">
          @if (_displayLines().length > 0) {
            <pre class="log-view__pre"><code>@for (line of _displayLines(); track $index) {
<span class="log-view__line">{{ line }}</span>
}</code></pre>
          } @else {
            <div class="log-view__empty" aria-label="No log output">No log output available.</div>
          }
        </div>
      }

      <!-- Persistent aria-live region: must NOT be inside @if to prevent suppression of announcements -->
      <div
        class="sr-only"
        aria-live="polite"
        aria-atomic="false"
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

  ngOnInit(): void {
    this._expanded.set(this.expanded());

    if (this.mode() === 'stream') {
      const stream = this.logStream();
      if (stream !== null) {
        stream.pipe(takeUntilDestroyed(this._destroyRef)).subscribe((line) => {
          this._streamedLines.update((prev) => [...prev, line]);
          this._liveAnnouncement.set(line);
        });
      }
    }
  }

  protected _toggle(): void {
    this._expanded.update((v) => !v);
  }
}
