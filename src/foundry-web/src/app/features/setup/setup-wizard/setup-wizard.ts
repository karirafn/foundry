import { Component, ChangeDetectionStrategy, WritableSignal, signal } from '@angular/core';
import { SetupAuthStepComponent } from '../setup-auth-step/setup-auth-step';
import { SetupAccountStepComponent } from '../setup-account-step/setup-account-step';
import { SetupReposStepComponent } from '../setup-repos-step/setup-repos-step';

type WizardStep = 1 | 2 | 3;

const STEP_LABELS: Record<WizardStep, string> = {
  1: 'Auth',
  2: 'Account',
  3: 'Repositories',
};

const STEPS: WizardStep[] = [1, 2, 3];

@Component({
  selector: 'fd-setup-wizard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SetupAuthStepComponent, SetupAccountStepComponent, SetupReposStepComponent],
  template: `
    <div class="setup-wizard">
      <nav class="setup-wizard__progress" aria-label="Setup progress">
        @for (step of steps; track step) {
          <div
            class="setup-wizard__step"
            [class.setup-wizard__step--active]="step === _currentStep()"
            [class.setup-wizard__step--completed]="step < _currentStep()"
            [attr.aria-current]="step === _currentStep() ? 'step' : null"
          >{{ stepLabels[step] }}</div>
        }
      </nav>

      <div class="setup-wizard__body">
        @switch (_currentStep()) {
          @case (1) {
            <fd-setup-auth-step (complete)="onAuthComplete()" />
          }
          @case (2) {
            <fd-setup-account-step (back)="onBack()" (complete)="onAccountComplete($event)" />
          }
          @case (3) {
            <fd-setup-repos-step [accountId]="createdAccountId()" (back)="onBack()" />
          }
        }
      </div>
    </div>
  `,
  styleUrl: './setup-wizard.scss',
})
export class SetupWizardComponent {
  protected readonly _currentStep: WritableSignal<WizardStep> = signal(1);

  readonly createdAccountId: WritableSignal<string> = signal('');

  protected readonly steps = STEPS;
  protected readonly stepLabels = STEP_LABELS;

  onAuthComplete(): void {
    this._currentStep.set(2);
  }

  onAccountComplete(accountId: string): void {
    this.createdAccountId.set(accountId);
    this._currentStep.set(3);
  }

  onReposComplete(): void {
    // Navigation to /issues is handled by SetupReposStepComponent internally
  }

  onBack(): void {
    this._currentStep.update(step => (step > 1 ? ((step - 1) as WizardStep) : step));
  }
}
