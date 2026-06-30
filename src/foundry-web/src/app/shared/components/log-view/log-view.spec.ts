import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { LogViewComponent } from './log-view';

function createStaticComponent(lines: string[] | null, expanded = true) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [LogViewComponent],
  });
  const fixture = TestBed.createComponent(LogViewComponent);
  fixture.componentRef.setInput('mode', 'static');
  fixture.componentRef.setInput('lines', lines);
  fixture.componentRef.setInput('expanded', expanded);
  fixture.detectChanges();
  return fixture;
}

function createStreamComponent(stream: Subject<string>, expanded = true) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [LogViewComponent],
  });
  const fixture = TestBed.createComponent(LogViewComponent);
  fixture.componentRef.setInput('mode', 'stream');
  fixture.componentRef.setInput('logStream', stream.asObservable());
  fixture.componentRef.setInput('expanded', expanded);
  fixture.detectChanges();
  return fixture;
}

describe('LogViewComponent', () => {
  // Cycle 1: component creates
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createStaticComponent([]);

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  // Cycle 2: toggle button renders with aria-expanded
  it('should render a toggle button with aria-expanded=true when expanded', () => {
    // Arrange / Act
    const fixture = createStaticComponent(['line 1'], true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.log-view__toggle') as HTMLElement;
    expect(btn).toBeTruthy();
    expect(btn.getAttribute('aria-expanded')).toBe('true');
  });

  it('should render a toggle button with aria-expanded=false when collapsed', () => {
    // Arrange / Act
    const fixture = createStaticComponent(['line 1'], false);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.log-view__toggle') as HTMLElement;
    expect(btn.getAttribute('aria-expanded')).toBe('false');
  });

  // Cycle 3: log body visible when expanded
  it('should show the log body when expanded', () => {
    // Arrange / Act
    const fixture = createStaticComponent(['line 1'], true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const body = el.querySelector('.log-view__body');
    expect(body).toBeTruthy();
  });

  it('should hide the log body when collapsed', () => {
    // Arrange / Act
    const fixture = createStaticComponent(['line 1'], false);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const body = el.querySelector('.log-view__body');
    expect(body).toBeFalsy();
  });

  // Cycle 4: static mode renders provided lines
  it('should render each static log line', () => {
    // Arrange / Act
    const fixture = createStaticComponent(['first line', 'second line']);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const lines = el.querySelectorAll('.log-view__line');
    expect(lines.length).toBe(2);
    expect(lines[0].textContent).toContain('first line');
    expect(lines[1].textContent).toContain('second line');
  });

  // Cycle 5: static mode with null shows empty state
  it('should show empty state when lines is null', () => {
    // Arrange / Act
    const fixture = createStaticComponent(null);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const empty = el.querySelector('.log-view__empty');
    expect(empty).toBeTruthy();
  });

  it('should show empty state when lines array is empty', () => {
    // Arrange / Act
    const fixture = createStaticComponent([]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const empty = el.querySelector('.log-view__empty');
    expect(empty).toBeTruthy();
  });

  // Cycle 6: aria-live region is always present (not conditional)
  it('should always render the aria-live region regardless of expanded state', () => {
    // Arrange / Act
    const fixture = createStaticComponent(['line 1'], false);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const liveRegion = el.querySelector('[aria-live]');
    expect(liveRegion).toBeTruthy();
  });

  // Cycle 7: stream mode subscribes and appends lines
  it('should append streamed lines in stream mode', () => {
    // Arrange
    const stream = new Subject<string>();
    const fixture = createStreamComponent(stream);
    const el = fixture.nativeElement as HTMLElement;

    // Act
    stream.next('streamed line 1');
    stream.next('streamed line 2');
    fixture.detectChanges();

    // Assert
    const lines = el.querySelectorAll('.log-view__line');
    expect(lines.length).toBe(2);
    expect(lines[0].textContent).toContain('streamed line 1');
    expect(lines[1].textContent).toContain('streamed line 2');
  });

  // Cycle 8: toggle emits from button click
  it('should toggle expanded state when toggle button is clicked', () => {
    // Arrange
    const fixture = createStaticComponent(['line 1'], true);
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.log-view__toggle') as HTMLButtonElement;

    // Act
    btn.click();
    fixture.detectChanges();

    // Assert
    expect(btn.getAttribute('aria-expanded')).toBe('false');
  });

  // Cycle 9: aria-controls points to the log body id
  it('should set aria-controls on the toggle button pointing to the log body', () => {
    // Arrange / Act
    const fixture = createStaticComponent(['line 1'], true);
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.log-view__toggle') as HTMLButtonElement;
    const body = el.querySelector('.log-view__body') as HTMLElement;

    // Assert
    expect(btn.getAttribute('aria-controls')).toBeTruthy();
    expect(btn.getAttribute('aria-controls')).toBe(body.id);
  });
});
