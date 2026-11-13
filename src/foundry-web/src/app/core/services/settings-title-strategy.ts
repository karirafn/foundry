import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { TitleStrategy, RouterStateSnapshot } from '@angular/router';

const SETTINGS_SUFFIX = ' — Foundry Settings';
const APP_SUFFIX = ' — Foundry';
const SETTINGS_URL = /^\/settings(?:[/?#]|$)/;

@Injectable()
export class SettingsTitleStrategy extends TitleStrategy {
  private readonly _title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const pageTitle = this.buildTitle(snapshot);
    if (pageTitle !== undefined) {
      const suffix = SETTINGS_URL.test(snapshot.url) ? SETTINGS_SUFFIX : APP_SUFFIX;
      this._title.setTitle(`${pageTitle}${suffix}`);
    }
  }
}
