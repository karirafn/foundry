import {
  ApplicationRef,
  ComponentRef,
  EnvironmentInjector,
  Injectable,
  createComponent,
  inject,
} from '@angular/core';
import { ConfirmDialogComponent } from './confirm-dialog';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
}

interface ActiveConfirm {
  ref: ComponentRef<ConfirmDialogComponent>;
  resolve: (confirmed: boolean) => void;
}

/**
 * Imperative entry point to `fd-confirm-dialog` for callers without a template of their own
 * (e.g. route guards). Mounts the dialog on `document.body` and resolves `true` on confirm,
 * `false` on cancel/Escape. Opening a new confirmation dismisses any still-open one.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  private readonly _appRef = inject(ApplicationRef);
  private readonly _environmentInjector = inject(EnvironmentInjector);
  private _active: ActiveConfirm | null = null;

  confirm(options: ConfirmOptions): Promise<boolean> {
    this._settle(false);

    const ref = createComponent(ConfirmDialogComponent, {
      environmentInjector: this._environmentInjector,
    });
    ref.setInput('title', options.title);
    ref.setInput('message', options.message);
    if (options.confirmLabel !== undefined) {
      ref.setInput('confirmLabel', options.confirmLabel);
    }
    if (options.cancelLabel !== undefined) {
      ref.setInput('cancelLabel', options.cancelLabel);
    }
    ref.setInput('destructive', options.destructive ?? false);
    ref.setInput('open', true);

    return new Promise<boolean>((resolve) => {
      this._active = { ref, resolve };
      ref.instance.confirmed.subscribe(() => this._settle(true));
      ref.instance.dismissed.subscribe(() => this._settle(false));

      document.body.appendChild(ref.location.nativeElement);
      this._appRef.attachView(ref.hostView);
      ref.changeDetectorRef.detectChanges();
    });
  }

  private _settle(confirmed: boolean): void {
    const active = this._active;
    if (!active) {
      return;
    }
    this._active = null;
    this._appRef.detachView(active.ref.hostView);
    active.ref.destroy();
    (active.ref.location.nativeElement as HTMLElement).remove();
    active.resolve(confirmed);
  }
}
