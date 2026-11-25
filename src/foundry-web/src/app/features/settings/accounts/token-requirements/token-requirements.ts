import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { TokenPermissionRequirement, TokenRequirements, creationUrl } from '../account.model';

/**
 * The muted note a requirement shows after its label, or null when the label stands alone.
 * Unknown verification kinds show no note, so a newer server never breaks the list.
 */
export function requirementNote(requirement: TokenPermissionRequirement): string | null {
  switch (requirement.verification) {
    case 'role':
      return 'set on each repository, not on the token';
    case 'always-granted':
      return 'always granted';
    case 'unverifiable':
      return 'checked when used';
    case 'not-checked':
      return 'not checked';
    default:
      return null;
  }
}

/** One entry of the requirements list: a requirement on its own, or a run of consecutive requirements under a heading. */
export type RequirementEntry =
  | { readonly kind: 'item'; readonly permission: TokenPermissionRequirement }
  | { readonly kind: 'group'; readonly heading: string; readonly permissions: readonly TokenPermissionRequirement[] };

/** Merges consecutive requirements that share a group under one heading; ungrouped requirements stand alone. */
export function groupRequirements(permissions: readonly TokenPermissionRequirement[]): RequirementEntry[] {
  const entries: RequirementEntry[] = [];
  for (const permission of permissions) {
    const heading = permission.group ?? null;
    const last = entries[entries.length - 1];
    if (heading === null) {
      entries.push({ kind: 'item', permission });
    } else if (last?.kind === 'group' && last.heading === heading) {
      entries[entries.length - 1] = { ...last, permissions: [...last.permissions, permission] };
    } else {
      entries.push({ kind: 'group', heading, permissions: [permission] });
    }
  }
  return entries;
}

/** What an account token needs on a provider, as the account form and setup wizard show it. */
@Component({
  selector: 'fd-token-requirements',
  standalone: true,
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let req = requirements();
    <section class="token-requirements" [attr.aria-labelledby]="_headingId()">
      <h3 [id]="_headingId()" class="token-requirements__heading">
        <svg class="token-requirements__icon" aria-hidden="true" xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="10"></circle>
          <line x1="12" y1="16" x2="12" y2="12"></line>
          <line x1="12" y1="8" x2="12.01" y2="8"></line>
        </svg>
        Token requirements
      </h3>
      <p class="token-requirements__text">
        Create a <strong>{{ req.tokenTypeLabel }}</strong> with the following:
      </p>
      <ng-template #requirementItem let-permission>
        <li class="token-requirements__item" [attr.data-verification]="permission.verification">
          <span class="token-requirements__label">{{ permission.label }}</span>
          @if (_note(permission); as note) {
            <span class="token-requirements__note"><span class="token-requirements__note-separator" aria-hidden="true">·</span> {{ note }}</span>
          }
        </li>
      </ng-template>
      <ul class="token-requirements__list" aria-label="Required token permissions">
        @for (entry of _entries(); track $index + ':' + (entry.kind === 'group' ? entry.heading : entry.permission.label); let i = $index) {
          @if (entry.kind === 'group') {
            <li class="token-requirements__group">
              <span [id]="idPrefix() + '-group-' + i" class="token-requirements__group-heading">{{ entry.heading }}</span>
              <ul class="token-requirements__group-list" [attr.aria-labelledby]="idPrefix() + '-group-' + i">
                @for (permission of entry.permissions; track permission.label) {
                  <ng-container *ngTemplateOutlet="requirementItem; context: { $implicit: permission }" />
                }
              </ul>
            </li>
          } @else {
            <ng-container *ngTemplateOutlet="requirementItem; context: { $implicit: entry.permission }" />
          }
        }
      </ul>
      @if (req.closingNote) {
        <p class="token-requirements__closing-note">{{ req.closingNote }}</p>
      }
      @if (_hasUnverifiable()) {
        <p class="token-requirements__explanation">
          Foundry checks each permission on every repository before it starts work. Permissions marked "checked when used" can't be checked ahead of time, so a missing one fails the push that needs it.
        </p>
      }
      @if (req.resourceOwnerHint) {
        <p [id]="_ownerHintId()" class="token-requirements__owner-hint">{{ req.resourceOwnerHint }}</p>
      }
      @if (req.creationNote) {
        <p [id]="_creationNoteId()" class="token-requirements__creation-note">{{ req.creationNote }}</p>
      }
      @if (_createTokenUrl(); as url) {
        <a class="token-requirements__link" [href]="url" target="_blank" rel="noopener noreferrer"
           [attr.aria-describedby]="_linkDescribedBy()">
          Create token on {{ _resolvedHost() }}
          <span class="token-requirements__link-icon" aria-hidden="true">↗</span>
          <span class="sr-only">(opens in a new tab)</span>
        </a>
      } @else {
        <p class="token-requirements__link-hint">
          Enter a Base URL above to get a direct link to the token creation page.
        </p>
      }
    </section>
  `,
  styleUrl: './token-requirements.scss',
})
export class TokenRequirementsComponent {
  readonly requirements: InputSignal<TokenRequirements> = input.required<TokenRequirements>();
  readonly baseUrl: InputSignal<string> = input.required<string>();
  /** Prefixes the heading, owner-hint and creation-note ids, so two lists on one page never share an id. */
  readonly idPrefix: InputSignal<string> = input.required<string>();

  protected readonly _headingId: Signal<string> = computed(() => `${this.idPrefix()}-req-heading`);
  protected readonly _ownerHintId: Signal<string> = computed(() => `${this.idPrefix()}-owner-hint`);
  protected readonly _creationNoteId: Signal<string> = computed(() => `${this.idPrefix()}-creation-note`);

  protected readonly _linkDescribedBy: Signal<string | null> = computed(() => {
    const req = this.requirements();
    const ids = [
      ...(req.resourceOwnerHint ? [this._ownerHintId()] : []),
      ...(req.creationNote ? [this._creationNoteId()] : []),
    ];
    return ids.length > 0 ? ids.join(' ') : null;
  });

  protected readonly _hasUnverifiable: Signal<boolean> = computed(() =>
    this.requirements().permissions.some((permission) => permission.verification === 'unverifiable'),
  );

  protected readonly _note = requirementNote;

  protected readonly _entries: Signal<RequirementEntry[]> = computed(() =>
    groupRequirements(this.requirements().permissions),
  );

  protected readonly _resolvedHost: Signal<string> = computed(() => {
    try {
      return new URL(this.baseUrl()).host;
    } catch {
      return this.baseUrl();
    }
  });

  protected readonly _createTokenUrl: Signal<string | null> = computed(() =>
    creationUrl(this.requirements().creationUrlTemplate, this.baseUrl()),
  );
}
