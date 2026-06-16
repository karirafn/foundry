import { Component, ChangeDetectionStrategy, WritableSignal, signal } from '@angular/core';

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
            <div class="setup-wizard__step-content" data-step="1">
              <p>Configure worker authentication.</p>
            </div>
          }
          @case (2) {
            <div class="setup-wizard__step-content" data-step="2">
              <p>Add a provider account.</p>
            </div>
          }
          @case (3) {
            <div class="setup-wizard__step-content" data-step="3">
              <p>Select repositories to monitor.</p>
            </div>
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
    // Navigation to /issues will be handled when step 3 component is implemented
  }
}
