import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { RunStatsBarComponent } from './run-stats-bar';
import { WorkerRunTotalsService } from '../../workers/run-totals.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { RunTotals } from '../../workers/run-totals.model';

const mockSignalR = {
  on: () => {},
  onReconnected: () => {},
  connectionStatus: signal<'connected' | 'reconnecting' | 'disconnected'>('disconnected'),
};

const mockTotals: RunTotals = {
  runCount: 5,
  durationMs: 300000,
  numTurns: 20,
  totalCostUsd: 4.31,
  inputTokens: 10000,
  outputTokens: 5000,
};

const zeroTotals: RunTotals = {
  runCount: 0,
  durationMs: 0,
  numTurns: 0,
  totalCostUsd: 0,
  inputTokens: 0,
  outputTokens: 0,
};

function setup() {
  TestBed.configureTestingModule({
    imports: [RunStatsBarComponent],
    providers: [
      WorkerRunTotalsService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: IssueSignalRService, useValue: mockSignalR },
    ],
  });

  const fixture = TestBed.createComponent(RunStatsBarComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  const service = TestBed.inject(WorkerRunTotalsService);
  return { fixture, httpMock, service };
}

describe('RunStatsBarComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle 1: component creates and renders a section
  it('should create the component', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render a section element', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const section = el.querySelector('section.run-stats-bar');
    expect(section).toBeTruthy();
  });

  // Cycle 2: skeleton shown during loading
  it('should render skeleton pills when loading is true (before first fetch completes)', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    fixture.detectChanges();

    // Assert — loading signal is true, skeleton should be present
    expect(service.loading()).toBe(true);
    const el = fixture.nativeElement as HTMLElement;
    const skeleton = el.querySelector('.run-stats-bar__skeleton');
    expect(skeleton).toBeTruthy();

    // Cleanup
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
  });

  it('should hide skeleton when loading completes', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const skeleton = el.querySelector('.run-stats-bar__skeleton');
    expect(skeleton).toBeFalsy();
  });

  // Cycle 3: six stat cards render after load
  it('should render six stat cards after data loads', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const cards = el.querySelectorAll('.run-stats-bar__card');
    expect(cards.length).toBe(6);
  });

  // Cycle 4: correct formatted values displayed
  it('should display formatted run count', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain('5');
  });

  it('should display formatted duration', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
    fixture.detectChanges();

    // Assert — 300000ms = 5 minutes
    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain('5m');
  });

  it('should display formatted cost', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain('$4.31');
  });

  it('should display formatted input tokens', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain('10,000');
  });

  // Cycle 5: error state renders inline error
  it('should render inline error when service has error', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush('error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('.run-stats-bar__error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
  });

  it('should render retry button in error state', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush('error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.run-stats-bar__retry');
    expect(retryBtn).toBeTruthy();
  });

  it('should call service.fetch when retry button is clicked', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush('error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.run-stats-bar__retry') as HTMLElement;

    // Act
    retryBtn.click();
    fixture.detectChanges();

    // Assert — a second HTTP request was made
    const req = httpMock.expectOne((r) => r.url === '/api/workers/run-totals');
    req.flush(mockTotals);
  });

  // Cycle 6: scope toggle renders
  it('should render a scope radiogroup', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const radiogroup = el.querySelector('[role="radiogroup"]');
    expect(radiogroup).toBeTruthy();
  });

  it('should render "Current month" and "All time" radio options', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain('Current month');
    expect(text).toContain('All time');
  });

  it('should call setScope when scope radio changes', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    fixture.detectChanges();
    const spy = vi.spyOn(service, 'setScope');

    // Act — change to all
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const allRadio = Array.from(radios).find(r => r.value === 'all');
    allRadio?.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    // Assert
    expect(spy).toHaveBeenCalledWith('all');

    // Cleanup — flush any pending request
    const pending = httpMock.match((r) => r.url === '/api/workers/run-totals');
    pending.forEach(r => r.flush(mockTotals));
  });

  // Cycle 7: zero values show formatters (bare zeros, no empty message)
  it('should display $0.00 for cost when totals are zero', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(zeroTotals);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain('$0.00');
  });

  it('should display 0s for duration when totals are zero', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(zeroTotals);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain('0s');
  });

  // Cycle 8: section has accessible heading
  it('should have a visible h2 heading inside the section', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('section.run-stats-bar h2');
    expect(heading).toBeTruthy();
  });

  // Cycle 9: each card has sr-only full label
  it('should have sr-only labels on each card for screen readers', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const srOnlyEls = el.querySelectorAll('.sr-only');
    expect(srOnlyEls.length).toBeGreaterThan(0);
  });

  // Cycle 10: OnPush strategy
  it('should use OnPush change detection strategy', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const componentDef = (RunStatsBarComponent as any).ɵcmp;
    expect(componentDef?.onPush).toBe(true);
  });

  // Fix 3: grid div is keyboard-reachable on mobile via tabindex
  it('should render the stats grid with tabindex="0" for keyboard scrollability', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const grid = el.querySelector('.run-stats-bar__grid');
    expect(grid).toBeTruthy();
    expect(grid?.getAttribute('tabindex')).toBe('0');
  });

  it('should render the stats grid with role="group" and aria-label', () => {
    // Arrange
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const grid = el.querySelector('.run-stats-bar__grid');
    expect(grid?.getAttribute('role')).toBe('group');
    expect(grid?.getAttribute('aria-label')).toBe('Run statistics');
  });

  // Fix 4: caption span is not aria-hidden so heading accessible name includes it
  it('should not have aria-hidden on the scope caption span', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const caption = el.querySelector('.run-stats-bar__scope-caption');
    expect(caption).toBeTruthy();
    expect(caption?.hasAttribute('aria-hidden')).toBe(false);
  });

  // Fix 6: formatValue is accessible from the template (protected)
  it('should render formatted value via template for run count', () => {
    // Arrange — verifies formatValue is template-accessible (protected works in templates)
    const { fixture, httpMock, service } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);

    // Act
    fixture.detectChanges();

    // Assert — if protected formatValue were inaccessible the card values would be empty
    const el = fixture.nativeElement as HTMLElement;
    const cardValues = el.querySelectorAll('.run-stats-bar__card-value');
    expect(cardValues.length).toBeGreaterThan(0);
    const hasNonEmptyValue = Array.from(cardValues).some(v => (v.textContent ?? '').trim().length > 0);
    expect(hasNonEmptyValue).toBe(true);
  });

  // Fix 7: instance-scoped heading id — two instances must not share the same id
  it('should use unique heading ids across multiple instances', () => {
    // Arrange
    const { fixture: fixture1, httpMock: hm1 } = setup();
    fixture1.detectChanges();
    const id1 = (fixture1.nativeElement as HTMLElement).querySelector('h2')?.getAttribute('id');

    // Cleanup first instance's pending requests
    hm1.match(() => true).forEach(r => r.flush(mockTotals));

    // Create a second instance in a new TestBed to get a fresh component
    TestBed.resetTestingModule();
    const { fixture: fixture2, httpMock: hm2 } = setup();
    fixture2.detectChanges();
    const id2 = (fixture2.nativeElement as HTMLElement).querySelector('h2')?.getAttribute('id');

    hm2.match(() => true).forEach(r => r.flush(mockTotals));

    // Assert — ids differ between instances
    expect(id1).toBeTruthy();
    expect(id2).toBeTruthy();
    expect(id1).not.toBe(id2);
  });
});
