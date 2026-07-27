import { TestBed } from '@angular/core/testing';
import { StateBadgeComponent } from './state-badge';
import { IssueState } from '../../../features/issues/issue.model';

function createComponent(state: IssueState, failureClassification?: string) {
  TestBed.configureTestingModule({
    imports: [StateBadgeComponent],
  });
  const fixture = TestBed.createComponent(StateBadgeComponent);
  fixture.componentRef.setInput('state', state);
  if (failureClassification !== undefined) {
    fixture.componentRef.setInput('failureClassification', failureClassification);
  }
  fixture.detectChanges();
  return fixture;
}

describe('StateBadgeComponent', () => {
  // Cycle 1: component creates and renders a pill element
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createComponent('detected');

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render a span with role="img" so aria-label is honored', () => {
    // Arrange / Act
    const fixture = createComponent('detected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const span = el.querySelector('span');
    expect(span).toBeTruthy();
    expect(span?.getAttribute('role')).toBe('img');
  });

  // Cycle 2: label text for each state
  it('should display "DETECTED" for detected state', () => {
    // Arrange / Act
    const fixture = createComponent('detected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('DETECTED');
  });

  it('should display "QUEUED" for queued state', () => {
    // Arrange / Act
    const fixture = createComponent('queued');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('QUEUED');
  });

  it('should display "BLOCKED" for blocked state', () => {
    // Arrange / Act
    const fixture = createComponent('blocked');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('BLOCKED');
  });

  it('should display "IN PROGRESS" for in_progress state', () => {
    // Arrange / Act
    const fixture = createComponent('in_progress');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('IN PROGRESS');
  });

  it('should display "REVIEW" for review state', () => {
    // Arrange / Act
    const fixture = createComponent('review');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('REVIEW');
  });

  it('should display "UNCHANGED" for unchanged state', () => {
    // Arrange / Act
    const fixture = createComponent('unchanged');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('UNCHANGED');
  });

  it('should display "FAILED" for failed state', () => {
    // Arrange / Act
    const fixture = createComponent('failed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('FAILED');
  });

  it('should display "COMPLETED" for completed state', () => {
    // Arrange / Act
    const fixture = createComponent('completed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('COMPLETED');
  });

  it('should display "REV QUEUED" for revision_queued state', () => {
    // Arrange / Act
    const fixture = createComponent('revision_queued');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('REV QUEUED');
  });

  it('should display "REV IN PROGRESS" for revision_in_progress state', () => {
    // Arrange / Act
    const fixture = createComponent('revision_in_progress');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('REV IN PROGRESS');
  });

  it('should display "REV FAILED" for revision_failed state', () => {
    // Arrange / Act
    const fixture = createComponent('revision_failed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('REV FAILED');
  });

  // Cycle 3: aria-label is human-readable
  it('should set aria-label with human-readable state description', () => {
    // Arrange / Act
    const fixture = createComponent('in_progress');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.getAttribute('aria-label')).toBe('State: in progress');
  });

  it('should set aria-label for revision_in_progress', () => {
    // Arrange / Act
    const fixture = createComponent('revision_in_progress');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.getAttribute('aria-label')).toBe('State: revision in progress');
  });

  // Cycle 4: CSS class reflects state for color binding
  it('should apply a CSS class based on the current state', () => {
    // Arrange / Act
    const fixture = createComponent('completed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--completed')).toBe(true);
  });

  it('should apply CSS class for revision_queued state', () => {
    // Arrange / Act
    const fixture = createComponent('revision_queued');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--revision-queued')).toBe(true);
  });

  // Cycle 5: ineligible state
  it('should display "INELIGIBLE" for ineligible state', () => {
    // Arrange / Act
    const fixture = createComponent('ineligible');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('INELIGIBLE');
  });

  it('should set aria-label "State: not eligible for dispatch" for ineligible state', () => {
    // Arrange / Act
    const fixture = createComponent('ineligible');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.getAttribute('aria-label')).toBe('State: not eligible for dispatch');
  });

  it('should apply badge--ineligible CSS class for ineligible state', () => {
    // Arrange / Act
    const fixture = createComponent('ineligible');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--ineligible')).toBe(true);
  });

  // Cycle 6: continuable_failed state
  it('should display "CONT FAILED" for continuable_failed state', () => {
    // Arrange / Act
    const fixture = createComponent('continuable_failed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('CONT FAILED');
  });

  it('should set aria-label "State: continuable failed" for continuable_failed state', () => {
    // Arrange / Act
    const fixture = createComponent('continuable_failed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.getAttribute('aria-label')).toBe('State: continuable failed');
  });

  it('should apply badge--continuable-failed CSS class for continuable_failed state', () => {
    // Arrange / Act
    const fixture = createComponent('continuable_failed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--continuable-failed')).toBe(true);
  });

  // Cycle 7: continuation_queued state
  it('should display "CONT QUEUED" for continuation_queued state', () => {
    // Arrange / Act
    const fixture = createComponent('continuation_queued');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('CONT QUEUED');
  });

  it('should set aria-label "State: continuation queued" for continuation_queued state', () => {
    // Arrange / Act
    const fixture = createComponent('continuation_queued');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.getAttribute('aria-label')).toBe('State: continuation queued');
  });

  it('should apply badge--continuation-queued CSS class for continuation_queued state', () => {
    // Arrange / Act
    const fixture = createComponent('continuation_queued');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--continuation-queued')).toBe(true);
  });

  // Cycle 8: usage_limited failure classification
  it('should display "USAGE LIMITED" when failureClassification is usage_limited and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'usage_limited');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('USAGE LIMITED');
  });

  it('should apply badge--usage-limited CSS class when failureClassification is usage_limited and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'usage_limited');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--usage-limited')).toBe(true);
  });

  it('should display "USAGE LIMITED" when failureClassification is usage_limited and state is continuable_failed', () => {
    // Arrange / Act
    const fixture = createComponent('continuable_failed', 'usage_limited');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('USAGE LIMITED');
  });

  it('should apply badge--usage-limited CSS class when failureClassification is usage_limited and state is continuable_failed', () => {
    // Arrange / Act
    const fixture = createComponent('continuable_failed', 'usage_limited');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--usage-limited')).toBe(true);
  });

  it('should display "NON-ZERO EXIT" when failureClassification is non_zero_exit and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'non_zero_exit');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('NON-ZERO EXIT');
  });

  it('should display "TIMED OUT" when failureClassification is timed_out and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'timed_out');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('TIMED OUT');
  });

  it('should display "CONTAINER ERROR" when failureClassification is container_error and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'container_error');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('CONTAINER ERROR');
  });

  it('should display "BOOTSTRAP FAILED" when failureClassification is worker_bootstrap_failed and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'worker_bootstrap_failed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('BOOTSTRAP FAILED');
  });

  it('should display "FAILED" when failureClassification is unknown and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'some_unknown_category');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('FAILED');
  });

  it('should set aria-label "State: usage limited" when failureClassification is usage_limited and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'usage_limited');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.getAttribute('aria-label')).toBe('State: usage limited');
  });

  // Cycle 9: provider_error failure classification
  it('should display "PROVIDER ERROR" when failureClassification is provider_error and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'provider_error');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('PROVIDER ERROR');
  });

  it('should apply badge--failure-provider-error CSS class when failureClassification is provider_error and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'provider_error');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--failure-provider-error')).toBe(true);
  });

  it('should set aria-label "State: provider error" when failureClassification is provider_error and state is failed', () => {
    // Arrange / Act
    const fixture = createComponent('failed', 'provider_error');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.getAttribute('aria-label')).toBe('State: provider error');
  });
});
