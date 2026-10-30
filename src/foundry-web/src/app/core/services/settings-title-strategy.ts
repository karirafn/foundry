import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { TitleStrategy, RouterStateSnapshot } from '@angular/router';

const TITLE_SUFFIX = ' — Foundry Settings';

@Injectable()
export class SettingsTitleStrategy extends TitleStrategy {
  private readonly _title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const pageTitle = this.buildTitle(snapshot);
    if (pageTitle !== undefined) {
      this._title.setTitle(`${pageTitle}${TITLE_SUFFIX}`);
    }
  }
}
