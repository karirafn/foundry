import { TestBed } from '@angular/core/testing';
import { RunKindTagComponent } from './run-kind-tag';
import { RunKind } from '../../utils/run-kind';

function render(kind: RunKind) {
  TestBed.configureTestingModule({ imports: [RunKindTagComponent] });
  const fixture = TestBed.createComponent(RunKindTagComponent);
  fixture.componentRef.setInput('kind', kind);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('RunKindTagComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  // A record over the whole union, so a new run kind without an expected label does not compile.
  const expectedLabels: Record<RunKind, string> = {
    triage: 'triage',
    fresh: 'implement',
    continuation: 'continue',
    review_fix: 'review fix',
    checks_fix: 'checks fix',
    conflict_fix: 'conflict',
    input_resume: 'resume',
  };

  it.each(Object.entries(expectedLabels) as [RunKind, string][])('should label the %s run kind "%s"', (kind, label) => {
    // Act
    const { el } = render(kind);

    // Assert
    expect(el.querySelector('.run-kind-tag')?.lastChild?.textContent).toBe(label);
  });

  it('should prefix the label with a screen-reader-only "Run: " label', () => {
    // Act
    const { el } = render('checks_fix');

    // Assert
    const tag = el.querySelector('.run-kind-tag');
    expect(tag?.firstElementChild?.classList.contains('sr-only')).toBe(true);
    expect(tag?.firstElementChild?.textContent).toBe('Run: ');
    expect(tag?.textContent).toBe('Run: checks fix');
  });

  it('should not be interactive', () => {
    // Act
    const { el } = render('fresh');

    // Assert
    expect(el.querySelector('a, button, [tabindex]')).toBeNull();
  });

  it('should update when the kind input changes', () => {
    // Arrange
    const { fixture, el } = render('fresh');

    // Act
    fixture.componentRef.setInput('kind', 'review_fix');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.run-kind-tag')?.textContent).toBe('Run: review fix');
  });
});
