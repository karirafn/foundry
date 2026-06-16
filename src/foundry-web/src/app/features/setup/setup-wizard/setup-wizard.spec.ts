import { TestBed } from '@angular/core/testing';
import { SetupWizardComponent } from './setup-wizard';

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SetupWizardComponent],
  });

  const fixture = TestBed.createComponent(SetupWizardComponent);
  return { fixture, component: fixture.componentInstance };
}

describe('SetupWizardComponent', () => {
  // Cycle 1: progress bar shows three step labels
  it('should render three step labels: Auth, Account, Repositories', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const steps = el.querySelectorAll('.setup-wizard__step');
    const labels = Array.from(steps).map((s) => s.textContent?.trim());
    expect(labels).toEqual(['Auth', 'Account', 'Repositories']);
  });

  // Cycle 2: first step is active on initial render
  it('should mark the first step as active initially', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const activeStep = el.querySelector('.setup-wizard__step--active');
    expect(activeStep?.textContent?.trim()).toBe('Auth');
  });

  // Cycle 3: onAuthComplete advances to step 2
  it('should advance to step 2 when onAuthComplete is called', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();

    // Act
    component.onAuthComplete();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const activeStep = el.querySelector('.setup-wizard__step--active');
    expect(activeStep?.textContent?.trim()).toBe('Account');
  });

  // Cycle 4: onAccountComplete stores accountId and advances to step 3
  it('should advance to step 3 and store accountId when onAccountComplete is called', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();
    component.onAuthComplete();
    fixture.detectChanges();

    // Act
    component.onAccountComplete('account-42');
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const activeStep = el.querySelector('.setup-wizard__step--active');
    expect(activeStep?.textContent?.trim()).toBe('Repositories');
    expect(component.createdAccountId()).toBe('account-42');
  });

  // Cycle 5: progress indicator marks completed steps
  it('should mark steps before the active step as completed', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();
    component.onAuthComplete();
    component.onAccountComplete('account-1');
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const completedSteps = el.querySelectorAll('.setup-wizard__step--completed');
    const labels = Array.from(completedSteps).map((s) => s.textContent?.trim());
    expect(labels).toEqual(['Auth', 'Account']);
  });

  // Cycle 6: step content area renders step-specific placeholder for each step
  it('should render the auth step content area on step 1', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const content = el.querySelector('.setup-wizard__step-content[data-step="1"]');
    expect(content).toBeTruthy();
  });

  it('should render the account step content area on step 2', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();

    // Act
    component.onAuthComplete();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const content = el.querySelector('.setup-wizard__step-content[data-step="2"]');
    expect(content).toBeTruthy();
  });

  it('should render the repositories step content area on step 3', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();
    component.onAuthComplete();
    fixture.detectChanges();

    // Act
    component.onAccountComplete('account-1');
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const content = el.querySelector('.setup-wizard__step-content[data-step="3"]');
    expect(content).toBeTruthy();
  });

  // Cycle 7: progress bar has correct aria-label for accessibility
  it('should render the progress bar nav with an accessible aria-label', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const nav = el.querySelector('nav.setup-wizard__progress');
    expect(nav).toBeTruthy();
    expect(nav?.getAttribute('aria-label')).toBe('Setup progress');
  });

  // Cycle 8: aria-current on active step
  it('should set aria-current="step" on the active step', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const activeStep = el.querySelector('.setup-wizard__step--active');
    expect(activeStep?.getAttribute('aria-current')).toBe('step');
  });
});
