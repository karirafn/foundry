import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  Signal,
  ViewChild,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { ConfirmDialogComponent } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import { PackageSourceService } from '../package-source.service';
import { PackageSourceListComponent } from '../package-source-list/package-source-list';
import { PackageSourceCredentialFormComponent } from '../package-source-credential-form/package-source-credential-form';
import { CredentialField, PackageSource, SaveCredentialRequest, packageSourceAnchorId } from '../package-source.model';

type PackageSourceView = { kind: 'list' } | { kind: 'edit'; source: PackageSource };

/** Identifies the button that opened the form, so Cancel can return focus to it once the list re-renders. */
interface Opener {
  className: string;
  ariaLabel: string;
}

@Component({
  selector: 'fd-settings-package-sources',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ConfirmDialogComponent, PackageSourceListComponent, PackageSourceCredentialFormComponent],
  template: `
    <div class="package-sources-settings">
      <span class="sr-only" role="status" aria-live="polite" aria-atomic="true">{{ service.announcement() }}</span>
      <section class="package-sources-settings__section">
        <h2 class="package-sources-settings__section-title" #sectionHeading tabindex="-1">Package sources</h2>
        <p class="package-sources-settings__section-description">
          Private NuGet sources declared in the root nuget.config of your monitored repositories. Foundry discovers them
          automatically and checks that it can read each one.
        </p>
        <p class="package-sources-settings__section-description">
          Foundry reads a source with its package source credential when one is registered here, otherwise with the
          account credential that covers the repository's namespace. A source on another host than the repository's
          provider is read without a credential.
        </p>
        @if (_targetMissing()) {
          <p class="package-sources-settings__target-missing" role="status">
            That package source is no longer declared by a monitored repository.
          </p>
        }

        @switch (_view().kind) {
          @case ('list') {
            <fd-package-source-list
              [sources]="service.sources()"
              [loading]="service.loading()"
              [error]="service.loadError()"
              [removingId]="service.removingId()"
              [pendingChecks]="service.pendingChecks()"
              [targetUrl]="_targetUrl()"
              (editCredential)="onEditCredential($event)"
              (removeCredential)="onRemoveCredential($event)"
              (retry)="service.load()"
            />
            <div class="package-sources-settings__remove-error" role="alert">{{ service.removeError() ?? '' }}</div>
            <fd-confirm-dialog
              [open]="_pendingRemoval() !== null"
              title="Remove credential?"
              [message]="_removeMessage()"
              confirmLabel="Remove credential"
              cancelLabel="Keep credential"
              [destructive]="true"
              (confirmed)="confirmRemove()"
              (dismissed)="cancelRemove()"
            />
          }
          @case ('edit') {
            <fd-package-source-credential-form
              [source]="_editSource()!"
              [saving]="service.saving()"
              [fieldErrors]="service.fieldErrors()"
              [saveError]="service.saveError()"
              (save)="onSave($event)"
              (cancel)="onCancel()"
              (fieldEdited)="onFieldEdited($event)"
            />
          }
        }
      </section>
    </div>
  `,
  styleUrl: './settings-package-sources.scss',
})
export class SettingsPackageSourcesComponent implements OnInit {
  protected readonly service = inject(PackageSourceService);
  private readonly _injector = inject(Injector);
  private readonly _host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly _route = inject(ActivatedRoute);

  @ViewChild('sectionHeading') private readonly _sectionHeading?: ElementRef<HTMLElement>;

  protected readonly _view: WritableSignal<PackageSourceView> = signal({ kind: 'list' });
  protected readonly _pendingRemoval: WritableSignal<PackageSource | null> = signal(null);
  protected readonly _targetMissing: WritableSignal<boolean> = signal(false);

  protected readonly _targetUrl: Signal<string | null> = toSignal(
    this._route.queryParamMap.pipe(map((params) => params.get('source'))),
    { initialValue: null },
  );

  protected readonly _editSource: Signal<PackageSource | null> = computed(() => {
    const view = this._view();
    return view.kind === 'edit' ? view.source : null;
  });

  protected readonly _removeMessage: Signal<string> = computed(() => {
    const source = this._pendingRemoval();
    return source
      ? `Remove the credential for ${source.url}? Foundry falls back to the account credential for each repository that declares this source, and re-checks the source now.`
      : '';
  });

  private _opener: Opener | null = null;

  constructor() {
    effect(() => {
      if (!this.service.saveSuccess()) {
        return;
      }
      untracked(() => {
        const source = this._editSource();
        this.service.clearSaveState();
        this._view.set({ kind: 'list' });
        if (source) {
          this._afterRender(() => this._focusCard(source.url));
        }
      });
    });
  }

  ngOnInit(): void {
    this.service.clearSaveState();
    this.service.load().then((outcome) => {
      const target = this._targetUrl();
      if (outcome !== 'loaded' || target === null) {
        return;
      }
      if (this.service.sources().some((s) => s.url === target)) {
        this._afterRender(() => this._scrollToCard(target));
      } else {
        this._targetMissing.set(true);
        this._afterRender(() => this._sectionHeading?.nativeElement.focus());
      }
    });
  }

  onEditCredential(source: PackageSource): void {
    const active = document.activeElement;
    this._opener =
      active instanceof HTMLElement && active.getAttribute('aria-label') !== null
        ? { className: active.classList[0] ?? '', ariaLabel: active.getAttribute('aria-label')! }
        : null;
    this.service.clearSaveState();
    this._view.set({ kind: 'edit', source });
    this._afterRender(() =>
      this._host.nativeElement.querySelector<HTMLElement>('.package-source-credential-form__heading')?.focus(),
    );
  }

  onRemoveCredential(source: PackageSource): void {
    this._pendingRemoval.set(source);
  }

  async confirmRemove(): Promise<void> {
    const source = this._pendingRemoval();
    this._pendingRemoval.set(null);
    if (source === null) {
      return;
    }
    if (await this.service.removeCredential(source.id)) {
      this._afterRender(() => this._focusCard(source.url));
    }
  }

  cancelRemove(): void {
    this._pendingRemoval.set(null);
  }

  onSave(request: SaveCredentialRequest): void {
    const source = this._editSource();
    if (source) {
      this.service.saveCredential(source.id, request);
    }
  }

  onFieldEdited(field: CredentialField): void {
    this.service.clearFieldError(field);
  }

  onCancel(): void {
    const opener = this._opener;
    this._opener = null;
    this.service.clearSaveState();
    this._view.set({ kind: 'list' });
    this._afterRender(() => {
      const button = opener ? this._findOpener(opener) : null;
      if (button) {
        button.focus();
      } else {
        this._sectionHeading?.nativeElement.focus();
      }
    });
  }

  private _findOpener(opener: Opener): HTMLElement | null {
    const candidates = this._host.nativeElement.querySelectorAll<HTMLElement>('[aria-label]');
    return (
      Array.from(candidates).find(
        (el) => el.getAttribute('aria-label') === opener.ariaLabel && el.classList.contains(opener.className),
      ) ?? null
    );
  }

  private _cardUrlSpan(url: string): HTMLElement | null {
    return this._host.nativeElement.querySelector<HTMLElement>(
      `#${packageSourceAnchorId(url)} .package-source-list__url`,
    );
  }

  private _focusCard(url: string): void {
    this._cardUrlSpan(url)?.focus();
  }

  private _scrollToCard(url: string): void {
    const span = this._cardUrlSpan(url);
    if (!span) {
      return;
    }
    const card = span.closest('li') ?? span;
    const reduceMotion =
      typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (typeof card.scrollIntoView === 'function') {
      card.scrollIntoView({ block: 'center', behavior: reduceMotion ? 'auto' : 'smooth' });
    }
    span.focus({ preventScroll: true });
  }

  private _afterRender(fn: () => void): void {
    afterNextRender(fn, { injector: this._injector });
  }
}
