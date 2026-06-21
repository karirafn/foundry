import { TestBed } from '@angular/core/testing';
import { ProviderIconComponent } from './provider-icon';

function setup(providerType: string) {
  TestBed.configureTestingModule({
    imports: [ProviderIconComponent],
  });
  const fixture = TestBed.createComponent(ProviderIconComponent);
  fixture.componentRef.setInput('providerType', providerType);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('ProviderIconComponent', () => {
  // Cycle 1: component creates and host element has role="img"
  it('should create the component', () => {
    // Arrange / Act
    const { fixture } = setup('github');

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render a host element with role="img"', () => {
    // Arrange / Act
    const { el } = setup('github');

    // Assert
    expect(el.getAttribute('role')).toBe('img');
  });

  // Cycle 2: aria-label shows "GitHub" for github providerType
  it('should set aria-label to "GitHub" for github providerType', () => {
    // Arrange / Act
    const { el } = setup('github');

    // Assert
    expect(el.getAttribute('aria-label')).toBe('GitHub');
  });

  it('should set aria-label to "GitLab" for gitlab providerType', () => {
    // Arrange / Act
    const { el } = setup('gitlab');

    // Assert
    expect(el.getAttribute('aria-label')).toBe('GitLab');
  });

  // Cycle 3: inner SVG is aria-hidden
  it('should render an inner svg that is aria-hidden', () => {
    // Arrange / Act
    const { el } = setup('github');

    // Assert
    const svg = el.querySelector('svg');
    expect(svg).toBeTruthy();
    expect(svg?.getAttribute('aria-hidden')).toBe('true');
  });

  // Cycle 4: case-insensitive input — "GitHub" (mixed case) resolves to github label
  it('should handle mixed-case providerType defensively', () => {
    // Arrange / Act
    const { el } = setup('GitHub');

    // Assert
    expect(el.getAttribute('aria-label')).toBe('GitHub');
  });

  // Cycle 5: unknown provider falls back to a non-empty aria-label
  it('should render a fallback aria-label for unknown providerType', () => {
    // Arrange / Act
    const { el } = setup('bitbucket');

    // Assert
    expect(el.getAttribute('aria-label')).toBe('bitbucket');
    const svg = el.querySelector('svg');
    expect(svg).toBeTruthy();
  });
});
