import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SettingsService } from '../../../core/services/settings.service';
import { AuthSettings } from '../../../core/models/settings.model';

interface OAuthPresentView {
  kind: 'oauth';
  email: string;
  monogram: string;
  title: string;
  ariaLabel: string;
}

interface ReLoginView {
  kind: 'oauthReLogin';
  email: string | null;
  monogram: string;
  title: string;
  ariaLabel: string;
}

interface ApiKeyView {
  kind: 'apiKey';
  title: string;
  ariaLabel: string;
}

interface HiddenView {
  kind: 'hidden';
}

type AccountChipView = OAuthPresentView | ReLoginView | ApiKeyView | HiddenView;

function buildTitle(org: string | null, subscription: string | null, email: string): string {
  if (org && subscription) {
    return `${org} · ${subscription}`;
  }
  if (org) {
    return org;
  }
  if (subscription) {
    return subscription;
  }
  return email;
}

function toMonogram(email: string | null): string {
  const trimmed = email?.trim() ?? '';
  return trimmed.length > 0 ? trimmed.charAt(0).toUpperCase() : '?';
}

function mapToView(authSettings: AuthSettings | null): AccountChipView {
  if (authSettings === null) {
    return { kind: 'hidden' };
  }

  const { mode, oauth, accountEmail, accountOrgName } = authSettings;

  if (mode === 'oauth') {
    if (oauth === null || oauth.status === 'NotConfigured' || accountEmail === null) {
      return { kind: 'hidden' };
    }

    if (oauth.status === 'Present') {
      return {
        kind: 'oauth',
        email: accountEmail,
        monogram: toMonogram(accountEmail),
        title: buildTitle(accountOrgName, oauth.subscriptionType, accountEmail),
        ariaLabel: `Worker account: ${accountEmail}. Open settings.`,
      };
    }

    if (oauth.status === 'ReLoginNeeded') {
      return {
        kind: 'oauthReLogin',
        email: accountEmail,
        monogram: toMonogram(accountEmail),
        title: 'Re-login needed',
        ariaLabel: 'Worker account needs re-login. Open settings.',
      };
    }

    return { kind: 'hidden' };
  }

  return {
    kind: 'apiKey',
    title: 'Workers run with an API key',
    ariaLabel: 'Workers run with an API key. Open settings.',
  };
}

@Component({
  selector: 'fd-account-chip',
  imports: [RouterLink],
  templateUrl: './account-chip.html',
  styleUrl: './account-chip.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountChipComponent {
  private readonly _settingsService = inject(SettingsService);

  readonly view = computed<AccountChipView>(() =>
    mapToView(this._settingsService.authSettings())
  );
}
