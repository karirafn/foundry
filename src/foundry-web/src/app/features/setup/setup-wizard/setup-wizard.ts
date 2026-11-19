import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { SettingsService } from '../../../core/services/settings.service';
import { AccountService } from '../../settings/accounts/account.service';
import { SetupAuthStepComponent } from '../setup-auth-step/setup-auth-step';
import { SetupAccountStepComponent } from '../setup-account-step/setup-account-step';
import { SetupWebhookStepComponent } from '../setup-webhook-step/setup-webhook-step';
import { SetupReposStepComponent } from '../setup-repos-step/setup-repos-step';

type WizardStep = 1 | 2 | 3 | 4;

const STEP_LABELS: Record<WizardStep, string> = {
  1: 'Auth',
  2: 'Account',
  3: 'Webhooks',
  4: 'Repositories',
};

const STEPS: WizardStep[] = [1, 2, 3, 4];

const RESUME_NOTES: Partial<Record<WizardStep, string>> = {
  3: 'Foundry now needs a public webhook URL. Your Claude authentication and provider account are already set up.',
  4: 'Claude authentication, your provider account and the webhook URL are already set up. Add repositories now, or skip.',
};

@Component({
  selector: 'fd-setup-wizard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SetupAuthStepComponent, SetupAccountStepComponent, SetupWebhookStepComponent, SetupReposStepComponent],
  template: `
    <div class="setup-wizard">
      <h1 class="setup-wizard__title">Foundry Setup</h1>

      <nav class="setup-wizard__progress" aria-label="Setup progress">
        <ol class="setup-wizard__steps">
          @for (step of steps; track step) {
            <li
              class="setup-wizard__step"
              [class.setup-wizard__step--active]="step === _currentStep()"
              [class.setup-wizard__step--completed]="step < _currentStep()"
              [attr.aria-current]="step === _currentStep() ? 'step' : null"
            >
              @if (step < _currentStep()) {
                <span class="setup-wizard__step-check" aria-hidden="true">✓</span>
              }
              <span class="setup-wizard__step-label">{{ stepLabels[step] }}</span>
              @if (step < _currentStep()) {
                <span class="sr-only">, completed</span>
              }
            </li>
          }
        </ol>
      </nav>

      @if (resumeNote; as note) {
        <p class="setup-wizard__resume-note">{{ note }}</p>
      }

      <div class="setup-wizard__body">
        @switch (_currentStep()) {
          @case (1) {
            <fd-setup-auth-step (complete)="onAuthComplete()" />
          }
          @case (2) {
            <fd-setup-account-step (back)="onBack()" (complete)="onAccountComplete($event)" />
          }
          @case (3) {
            <fd-setup-webhook-step [showBack]="3 > _firstStep" (back)="onBack()" (complete)="onWebhookComplete()" />
          }
          @case (4) {
            <fd-setup-repos-step
              [accountId]="_reposAccountId()"
              [showBack]="4 > _firstStep"
              (back)="onBack()"
              (complete)="onReposComplete()"
            />
          }
        }
      </div>
    </div>
  `,
  styleUrl: './setup-wizard.scss',
})
export class SetupWizardComponent {
  private readonly _router = inject(Router);
  private readonly _injector = inject(Injector);
  private readonly _host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly _accountService = inject(AccountService);
  private readonly _settingsService = inject(SettingsService);

  /** Where this visit opened, computed once from the app-start loads so a resumed operator never reaches the Account form. */
  protected readonly _firstStep: WizardStep = this._resolveFirstStep();

  protected readonly _currentStep: WritableSignal<WizardStep> = signal(this._firstStep);

  private readonly _createdAccountId: WritableSignal<string> = signal('');
  readonly createdAccountId: Signal<string> = this._createdAccountId.asReadonly();

  protected readonly _reposAccountId: Signal<string> = computed(
    () => this._createdAccountId() || (this._accountService.accounts()[0]?.id ?? ''),
  );

  protected readonly steps = STEPS;
  protected readonly stepLabels = STEP_LABELS;
  protected readonly resumeNote: string | null = RESUME_NOTES[this._firstStep] ?? null;

  onAuthComplete(): void {
    this._goTo(2);
  }

  onAccountComplete(accountId: string): void {
    this._createdAccountId.set(accountId);
    this._goTo(3);
  }

  onWebhookComplete(): void {
    this._goTo(4);
  }

  onReposComplete(): void {
    this._router.navigate(['/issues']);
  }

  onBack(): void {
    const step = this._currentStep();
    if (step > this._firstStep) {
      this._goTo((step - 1) as WizardStep);
    }
  }

  private _resolveFirstStep(): WizardStep {
    if (this._accountService.accounts().length === 0) {
      return 1;
    }
    return this._settingsService.settings()?.webhookPublicBaseUrl ? 4 : 3;
  }

  private _goTo(step: WizardStep): void {
    this._currentStep.set(step);
    afterNextRender(
      () => this._host.nativeElement.querySelector<HTMLElement>('.setup-wizard__body h2')?.focus(),
      { injector: this._injector },
    );
  }
}
