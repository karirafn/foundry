import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { SetupAuthStepComponent } from './setup-auth-step';

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SetupAuthStepComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  const fixture = TestBed.createComponent(SetupAuthStepComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, component: fixture.componentInstance, httpMock };
}

describe('SetupAuthStepComponent', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // Cycle 1: renders API key input and Next button
  it('should render an API key input and a Next button', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('input[type="password"]');
    const button = el.querySelector('button');
    expect(input).toBeTruthy();
    expect(button?.textContent?.trim()).toBe('Next');
  });

  // Cycle 2: Next button is disabled when input is empty
  it('should disable the Next button when the API key input is empty', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const button = el.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });

  // Cycle 3: Next button is enabled when input has a value
  it('should enable the Next button when the API key input has a value', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('input') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const button = el.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBe(false);
  });

  // Cycle 4: Next button is disabled while saving
  it('should disable the Next button while saving', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('input') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const button = el.querySelector('button') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    // Assert
    expect(button.disabled).toBe(true);

    // Cleanup
    httpMock.expectOne('/api/settings/auth').flush({
      authMode: 'ApiKey',
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      maxConcurrent: 3,
      timeoutMinutes: 60,
      systemPromptTemplate: null,
      workerPromptTemplate: null,
    });
  });

  // Cycle 5: emits complete output on successful save
  it('should emit the complete event after a successful save', async () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('input') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const button = el.querySelector('button') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/settings/auth').flush({
      authMode: 'ApiKey',
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      maxConcurrent: 3,
      timeoutMinutes: 60,
      systemPromptTemplate: null,
      workerPromptTemplate: null,
    });
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 6: shows error message on save failure
  it('should display an error message when the save fails', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('input') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const button = el.querySelector('button') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/settings/auth').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const errorEl = el.querySelector('[role="alert"]');
    expect(errorEl?.textContent?.trim()).toBeTruthy();
  });

  // Cycle 7: does not emit complete on failure
  it('should not emit the complete event when the save fails', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('input') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const button = el.querySelector('button') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/settings/auth').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(false);
  });
});
