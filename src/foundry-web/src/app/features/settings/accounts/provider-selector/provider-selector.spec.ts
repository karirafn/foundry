import { TestBed } from '@angular/core/testing';
import { ProviderSelectorComponent } from './provider-selector';
import { ProviderType } from '../account.model';

function setup(overrides: {
  provider?: ProviderType;
  disabled?: boolean;
} = {}) {
  const fixture = TestBed.createComponent(ProviderSelectorComponent);
  fixture.componentRef.setInput('provider', overrides.provider ?? 'GitHub');
  fixture.componentRef.setInput('disabled', overrides.disabled ?? false);
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
    // Arrange / Act
    const { el } = setup();

    // Assert
    const group = el.querySelector('[role="radiogroup"]');
    expect(group).toBeTruthy();
    const radios = el.querySelectorAll('input[type="radio"]');
    expect(radios.length).toBe(2);
  });

  // Cycle 2: GitHub radio is checked when provider is GitHub
  it('should check GitHub radio when provider is GitHub', () => {
    // Arrange / Act
    const { el } = setup({ provider: 'GitHub' });

    // Assert
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const githubRadio = Array.from(radios).find((r) => r.value === 'GitHub');
    expect(githubRadio?.checked).toBe(true);
  });

  // Cycle 3: GitLab radio is checked when provider is GitLab
  it('should check GitLab radio when provider is GitLab', () => {
    // Arrange / Act
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
    // Arrange / Act
    const { el } = setup({ disabled: true });

    // Assert
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    radios.forEach((r) => expect(r.disabled).toBe(true));
  });

  // Cycle 7: radiogroup has aria-label
  it('should have aria-label on the radiogroup', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const group = el.querySelector('[role="radiogroup"]');
    expect(group?.getAttribute('aria-label')).toBeTruthy();
  });
});
