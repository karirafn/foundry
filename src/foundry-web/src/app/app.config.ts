import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, TitleStrategy } from '@angular/router';

import { routes } from './app.routes';
import { SettingsService } from './core/services/settings.service';
import { AccountService } from './features/settings/accounts/account.service';
import { SettingsTitleStrategy } from './core/services/settings-title-strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(),
    { provide: TitleStrategy, useClass: SettingsTitleStrategy },
    provideAppInitializer(async () => {
      const settingsService = inject(SettingsService);
      const accountService = inject(AccountService);
      await Promise.all([
        settingsService.loadSettings(),
        accountService.loadAccounts(),
      ]);
    }),
  ]
};
