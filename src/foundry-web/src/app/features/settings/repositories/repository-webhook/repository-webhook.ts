import {
  ChangeDetectionStrategy,
  Component,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { RepositoryService } from '../repository.service';
import { RepositoryWebhookInfo, webhookStatusLabel } from '../repository.model';

/**
 * Webhook health and manual registration for one repository (ADR 0085). Shows the Webhook Status
 * and, while the hook is not active, the payload URL, content type, on-demand secret and exact
 * event list, plus the "I've added the webhook" action.
 */
@Component({
  selector: 'fd-repository-webhook',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <section class="repository-webhook" aria-labelledby="repository-webhook-heading">
      <h2 id="repository-webhook-heading" class="repository-webhook__heading">Webhook</h2>

      @if (_loadError()) {
        <p class="repository-webhook__error" role="alert">{{ _loadError() }}</p>
      } @else if (_info(); as info) {
        <p class="repository-webhook__status">
          <span class="repository-webhook__status-label">Status:</span>
          <span class="repository-webhook__status-value" [attr.data-status]="info.status">{{ _statusLabel() }}</span>
        </p>

        @switch (info.status) {
          @case ('not-configured') {
            <p class="repository-webhook__hint">
              Webhooks are off. Set the webhook port in the host configuration (<code>Webhooks:Port</code>) and a
              Webhook Public Base URL in <a routerLink="/settings/dispatch">Dispatch settings</a>. Until then this
              repository polls at its normal interval.
            </p>
          }
          @case ('active') {
            <p class="repository-webhook__hint">
              Deliveries are arriving. Foundry runs the matching poll pass right away and otherwise polls this
              repository every {{ info.backstopIntervalMinutes }} minutes.
            </p>
          }
          @default {
            @if (info.status === 'pending') {
              <p class="repository-webhook__hint">
                Waiting for the first delivery. If you already added the webhook, redeliver the <code>ping</code>
                event from the webhook's "Recent Deliveries" tab on GitHub.
              </p>
            }
            @if (info.status === 'silent') {
              <p class="repository-webhook__hint">
                A scheduled poll found a change no delivery announced, so this repository is back on its normal
                poll interval. The next verified delivery makes the webhook active again.
              </p>
            }

            <dl class="repository-webhook__details">
              <dt>Payload URL</dt>
              <dd><code class="repository-webhook__value">{{ info.payloadUrl }}</code></dd>

              <dt>Content type</dt>
              <dd><code class="repository-webhook__value">{{ info.contentType }}</code></dd>

              <dt>Secret</dt>
              <dd>
                @if (_secret(); as secret) {
                  <code class="repository-webhook__value repository-webhook__secret">{{ secret }}</code>
                } @else {
                  <button
                    type="button"
                    class="repository-webhook__btn"
                    [disabled]="_revealing()"
                    (click)="onRevealSecret()"
                  >{{ _revealing() ? 'Revealing...' : 'Reveal secret' }}</button>
                }
              </dd>

              <dt>Events</dt>
              <dd>
                <p class="repository-webhook__events-hint">Choose "Let me select individual events" and tick exactly:</p>
                <ul class="repository-webhook__events">
                  @for (event of info.events; track event) {
                    <li><code>{{ event }}</code></li>
                  }
                </ul>
              </dd>
            </dl>

            @if (info.status !== 'pending') {
              <button
                type="button"
                class="repository-webhook__btn repository-webhook__btn--primary"
                [disabled]="_marking()"
                (click)="onMarkRegistered()"
              >{{ _marking() ? 'Saving...' : "I've added the webhook" }}</button>
            }
          }
        }

        <p class="repository-webhook__error" role="alert">{{ _actionError() ?? '' }}</p>
      }
    </section>
  `,
  styleUrl: './repository-webhook.scss',
})
export class RepositoryWebhookComponent {
  private readonly _repositoryService = inject(RepositoryService);

  readonly accountId = input.required<string>();
  readonly repositoryId = input.required<string>();

  protected readonly _info: WritableSignal<RepositoryWebhookInfo | null> = signal(null);
  protected readonly _loadError: WritableSignal<string | null> = signal(null);
  protected readonly _secret: WritableSignal<string | null> = signal(null);
  protected readonly _revealing: WritableSignal<boolean> = signal(false);
  protected readonly _marking: WritableSignal<boolean> = signal(false);
  protected readonly _actionError: WritableSignal<string | null> = signal(null);

  protected readonly _statusLabel: Signal<string> = computed(() => {
    const info = this._info();
    return info ? webhookStatusLabel(info.status) : '';
  });

  constructor() {
    effect(() => {
      const accountId = this.accountId();
      const repositoryId = this.repositoryId();
      this._secret.set(null);
      this._loadError.set(null);
      this._repositoryService.getWebhook(accountId, repositoryId).subscribe({
        next: info => this._info.set(info),
        error: () => this._loadError.set("Couldn't load the webhook details."),
      });
    });
  }

  onRevealSecret(): void {
    this._actionError.set(null);
    this._revealing.set(true);
    this._repositoryService.revealWebhookSecret(this.accountId(), this.repositoryId()).subscribe({
      next: response => {
        this._secret.set(response.secret);
        this._revealing.set(false);
      },
      error: () => {
        this._revealing.set(false);
        this._actionError.set("Couldn't reveal the secret. Please try again.");
      },
    });
  }

  onMarkRegistered(): void {
    this._actionError.set(null);
    this._marking.set(true);
    this._repositoryService.markWebhookRegistered(this.accountId(), this.repositoryId()).subscribe({
      next: info => {
        this._info.set(info);
        this._marking.set(false);
      },
      error: () => {
        this._marking.set(false);
        this._actionError.set("Couldn't save. Please try again.");
      },
    });
  }
}
