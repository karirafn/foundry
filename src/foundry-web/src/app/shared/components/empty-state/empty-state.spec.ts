import { TestBed } from '@angular/core/testing';
import { EmptyStateComponent } from './empty-state';

function createComponent() {
  TestBed.configureTestingModule({
    imports: [EmptyStateComponent],
  });
  const fixture = TestBed.createComponent(EmptyStateComponent);
  fixture.detectChanges();
  return fixture;
}

describe('EmptyStateComponent', () => {
  // Cycle 1: component creates with correct ARIA attributes
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createComponent();

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should have role="status" and aria-label="No tracked issues"', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const container = el.querySelector('.empty-state') as HTMLElement;

    // Assert
    expect(container?.getAttribute('role')).toBe('status');
    expect(container?.getAttribute('aria-label')).toBe('No tracked issues');
  });

  // Cycle 2: renders heading and subtext
  it('should render "No tracked issues" heading', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.empty-state__heading')?.textContent?.trim()).toBe('No tracked issues');
  });

  it('should render descriptive subtext', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const subtext = el.querySelector('.empty-state__subtext')?.textContent?.trim();
    expect(subtext).toBe('Issues tagged with the trigger label will appear here automatically.');
  });

  // Cycle 3: renders SVG icon
  it('should render an SVG icon', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const svg = el.querySelector('.empty-state__icon svg');
    expect(svg).toBeTruthy();
  });

  it('should set aria-hidden on the SVG icon', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const svg = el.querySelector('.empty-state__icon svg');
    expect(svg?.getAttribute('aria-hidden')).toBe('true');
  });
});
