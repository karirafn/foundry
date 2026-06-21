import { TestBed } from '@angular/core/testing';
import { ProviderSelectorComponent } from './provider-selector';
import { ProviderType } from '../account.model';

function setup(overrides: {
  provider?: ProviderType;
  disabled?: boolean;
  ariaLabelledBy?: string;
} = {}) {
  const fixture = TestBed.createComponent(ProviderSelectorComponent);
  fixture.componentRef.setInput('provider', overrides.provider ?? 'GitHub');
  fixture.componentRef.setInput('disabled', overrides.disabled ?? false);
  if (overrides.ariaLabelledBy !== undefined) {
    fixture.componentRef.setInput('ariaLabelledBy', overrides.ariaLabelledBy);
  }
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

describe('ProviderSelectorComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProviderSelectorComponent],
    }).compileComponents();
  });

  // Cycle 1: component renders a radio group
  it('should render a radiogroup with GitHub and GitLab options', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const group = el.querySelector('[role="radiogroup"]');
    expect(group).toBeTruthy();
    const radios = el.querySelectorAll('input[type="radio"]');
    expect(radios.length).toBe(2);
  });

  // Cycle 2: GitHub radio is checked when provider is GitHub
  it('should check GitHub radio when provider is GitHub', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ provider: 'GitHub' });

    // Assert
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const githubRadio = Array.from(radios).find((r) => r.value === 'GitHub');
    expect(githubRadio?.checked).toBe(true);
  });

  // Cycle 3: GitLab radio is checked when provider is GitLab
  it('should check GitLab radio when provider is GitLab', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ provider: 'GitLab' });

    // Assert
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab');
    expect(gitlabRadio?.checked).toBe(true);
  });

  // Cycle 4: selecting GitLab emits providerChange and defaultBaseUrlChange
  it('should emit providerChange with GitLab when GitLab radio is clicked', () => {
    // Arrange
    const { el, component } = setup({ provider: 'GitHub' });
    let emittedProvider: ProviderType | undefined;
    component.providerChange.subscribe((v: ProviderType) => { emittedProvider = v; });

    // Act
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab')!;
    gitlabRadio.click();

    // Assert
    expect(emittedProvider).toBe('GitLab');
  });

  it('should emit defaultBaseUrlChange with https://gitlab.com when GitLab is selected', () => {
    // Arrange
    const { el, component } = setup({ provider: 'GitHub' });
    let emittedUrl: string | undefined;
    component.defaultBaseUrlChange.subscribe((url: string) => { emittedUrl = url; });

    // Act
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab')!;
    gitlabRadio.click();

    // Assert
    expect(emittedUrl).toBe('https://gitlab.com');
  });

  // Cycle 5: selecting GitHub emits providerChange and defaultBaseUrlChange
  it('should emit defaultBaseUrlChange with https://github.com when GitHub is selected', () => {
    // Arrange
    const { el, component } = setup({ provider: 'GitLab' });
    let emittedUrl: string | undefined;
    component.defaultBaseUrlChange.subscribe((url: string) => { emittedUrl = url; });

    // Act
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const githubRadio = Array.from(radios).find((r) => r.value === 'GitHub')!;
    githubRadio.click();

    // Assert
    expect(emittedUrl).toBe('https://github.com');
  });

  // Cycle 6: disabled mode disables all radio buttons
  it('should disable all radio buttons when disabled is true', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ disabled: true });

    // Assert
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    radios.forEach((r) => expect(r.disabled).toBe(true));
  });

  // Cycle 7: radiogroup has aria-label as fallback when no ariaLabelledBy provided
  it('should have aria-label on the radiogroup when no ariaLabelledBy is provided', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const group = el.querySelector('[role="radiogroup"]');
    expect(group?.getAttribute('aria-label')).toBeTruthy();
  });

  // Cycle 8: ariaLabelledBy input propagates to inner radiogroup
  it('should set aria-labelledby on the radiogroup when ariaLabelledBy is provided', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ ariaLabelledBy: 'account-form-provider-label' });

    // Assert
    const group = el.querySelector('[role="radiogroup"]');
    expect(group?.getAttribute('aria-labelledby')).toBe('account-form-provider-label');
  });

  it('should remove aria-label from radiogroup when ariaLabelledBy is provided', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ ariaLabelledBy: 'account-form-provider-label' });

    // Assert
    const group = el.querySelector('[role="radiogroup"]');
    expect(group?.getAttribute('aria-label')).toBeNull();
  });

  // Cycle 9: each component instance has a unique radio name
  it('should use a unique name attribute per component instance', () => {
    // Arrange
    const fixture1 = TestBed.createComponent(ProviderSelectorComponent);
    fixture1.componentRef.setInput('provider', 'GitHub');
    fixture1.componentRef.setInput('disabled', false);
    fixture1.detectChanges();

    const fixture2 = TestBed.createComponent(ProviderSelectorComponent);
    fixture2.componentRef.setInput('provider', 'GitHub');
    fixture2.componentRef.setInput('disabled', false);
    fixture2.detectChanges();

    // Act
    const radios1 = fixture1.nativeElement.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const radios2 = fixture2.nativeElement.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const name1 = radios1[0].name;
    const name2 = radios2[0].name;

    // Assert
    expect(name1).toBeTruthy();
    expect(name2).toBeTruthy();
    expect(name1).not.toBe(name2);
  });
});
