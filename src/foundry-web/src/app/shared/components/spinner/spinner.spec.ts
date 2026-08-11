import { TestBed } from '@angular/core/testing';
import { SpinnerComponent } from './spinner';

function createComponent(size?: number) {
  TestBed.configureTestingModule({
    imports: [SpinnerComponent],
  });
  const fixture = TestBed.createComponent(SpinnerComponent);
  if (size !== undefined) {
    fixture.componentRef.setInput('size', size);
  }
  fixture.detectChanges();
  return fixture;
}

describe('SpinnerComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  // Cycle 1: renders aria-hidden span with no accessible name
  it('should render a single span with aria-hidden="true"', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const span = el.querySelector('span.spinner');
    expect(span).not.toBeNull();
    expect(span?.getAttribute('aria-hidden')).toBe('true');
  });

  it('should have no accessible name — no aria-label and no visible text content', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const span = el.querySelector('span.spinner');
    expect(span?.getAttribute('aria-label')).toBeNull();
    expect(span?.textContent?.trim()).toBe('');
  });

  // Cycle 2: default size is 14px
  it('should default size to 14 — span width and height inline styles are 14px', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const span = el.querySelector('span.spinner') as HTMLElement;
    expect(span?.style.width).toBe('14px');
    expect(span?.style.height).toBe('14px');
  });

  // Cycle 3: size input is reflected in inline styles
  it('should reflect size input in span width and height when set to 24', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Act
    fixture.componentRef.setInput('size', 24);
    fixture.detectChanges();

    // Assert
    const span = el.querySelector('span.spinner') as HTMLElement;
    expect(span?.style.width).toBe('24px');
    expect(span?.style.height).toBe('24px');
  });

  // Cycle 4: prefers-reduced-motion guard exists in component styles
  it('should contain a prefers-reduced-motion: reduce media rule that sets animation to none', () => {
    // Arrange / Act
    const fixture = createComponent();

    // Assert — walk the component's compiled style sheets for the reduced-motion guard
    const hostEl = fixture.nativeElement as HTMLElement;
    const ownerDoc = hostEl.ownerDocument;
    const styleSheets = Array.from(ownerDoc.styleSheets);

    const hasReducedMotionRule = styleSheets.some(sheet => {
      try {
        return Array.from(sheet.cssRules).some(rule => {
          if (rule instanceof CSSMediaRule) {
            const conditionText = rule.conditionText ?? (rule as CSSMediaRule).media?.mediaText ?? '';
            const matchesMedia = conditionText.includes('prefers-reduced-motion') && conditionText.includes('reduce');
            if (!matchesMedia) {
              return false;
            }
            return Array.from(rule.cssRules).some(innerRule => {
              const styleRule = innerRule as CSSStyleRule;
              return styleRule.style?.animation === 'none' || styleRule.style?.animationName === 'none';
            });
          }
          return false;
        });
      } catch {
        return false;
      }
    });

    expect(hasReducedMotionRule).toBe(true);
  });
});
