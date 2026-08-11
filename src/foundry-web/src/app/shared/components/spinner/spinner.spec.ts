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
  it('should include a prefers-reduced-motion guard in the compiled styles', () => {
    // Arrange / Act
    const fixture = createComponent();

    // Assert — Angular injects component styles as <style> elements; read their text
    // directly because JSDOM does not populate CSSOM cssRules for injected styleUrls.
    const styleEls = Array.from(fixture.nativeElement.ownerDocument.head.querySelectorAll('style')) as HTMLStyleElement[];
    const styleText = styleEls
      .map(styleEl => styleEl.textContent ?? '')
      .join('');

    expect(styleText).toContain('prefers-reduced-motion');
  });
});
