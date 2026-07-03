import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { Component } from '@angular/core';
import { vi } from 'vitest';
import { NEVER } from 'rxjs';
import { SetupWizardComponent } from './setup-wizard';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';

const mockSystemSignalR = { reconnected: NEVER, dispatchStateChanged: NEVER, loginSessionUpdate: NEVER, notifications: [] };

@Component({ template: '', standalone: true })
class StubIssuesComponent {}

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SetupWizardComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: 'issues', component: StubIssuesComponent }]),
      { provide: SystemSignalRService, useValue: mockSystemSignalR },
    ],
  });

  const fixture = TestBed.createComponent(SetupWizardComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, component: fixture.componentInstance, httpMock };
}

describe('SetupWizardComponent', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

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
    const { fixture, component, httpMock } = setup();
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
    // createdAccountId is a readonly Signal — it must not expose a set() method
    expect(typeof (component.createdAccountId as unknown as { set?: unknown }).set).toBe('undefined');

    // Cleanup
    httpMock.expectOne('/api/accounts/account-42/repositories/available-repositories').flush([]);
  });

  // Cycle 5: progress indicator marks completed steps
  it('should mark steps before the active step as completed', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();
    component.onAuthComplete();
    component.onAccountComplete('account-1');
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const completedSteps = el.querySelectorAll('.setup-wizard__step--completed');
    const labels = Array.from(completedSteps).map((s) => s.textContent?.trim());
    expect(labels).toEqual(['Auth', 'Account']);

    // Cleanup
    httpMock.expectOne('/api/accounts/account-1/repositories/available-repositories').flush([]);
  });

  // Cycle 6: step component rendered in each step
  it('should render the auth step component on step 1', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('fd-setup-auth-step')).toBeTruthy();
  });

  it('should render the account step component on step 2', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();

    // Act
    component.onAuthComplete();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('fd-setup-account-step')).toBeTruthy();
  });

  it('should render the repos step component on step 3', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();
    component.onAuthComplete();
    fixture.detectChanges();

    // Act
    component.onAccountComplete('account-1');
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('fd-setup-repos-step')).toBeTruthy();

    // Cleanup
    httpMock.expectOne('/api/accounts/account-1/repositories/available-repositories').flush([]);
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

  // Cycle 9: back navigation from step 2 returns to step 1
  it('should return to step 1 when onBack is called from step 2', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();
    component.onAuthComplete();
    fixture.detectChanges();

    // Act
    component.onBack();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const activeStep = el.querySelector('.setup-wizard__step--active');
    expect(activeStep?.textContent?.trim()).toBe('Auth');
  });

  // Cycle 11: onReposComplete navigates to /issues
  it('should navigate to /issues when onReposComplete is called', async () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    // Act
    component.onReposComplete();
    fixture.detectChanges();

    // Assert
    expect(navigateSpy).toHaveBeenCalledWith(['/issues']);
  });

  // Cycle 10: back navigation from step 3 returns to step 2
  it('should return to step 2 when onBack is called from step 3', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();
    component.onAuthComplete();
    fixture.detectChanges();
    component.onAccountComplete('account-1');
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/account-1/repositories/available-repositories').flush([]);

    // Act
    component.onBack();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const activeStep = el.querySelector('.setup-wizard__step--active');
    expect(activeStep?.textContent?.trim()).toBe('Account');
  });
});
