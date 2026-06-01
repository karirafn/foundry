import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { IssueListComponent } from './issue-list';
import { IssueService } from '../issue.service';
import { SignalRService } from '../../../core/services/signalr.service';
import { IssueSummary } from '../issue.model';

const mockSignalRService = {
  on: () => {},
  onReconnected: () => {},
  connectionStatus: signal<'connected' | 'reconnecting' | 'disconnected'>('disconnected'),
};

const mockSummary: IssueSummary = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'detected',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
};

function setupComponent() {
  TestBed.configureTestingModule({
    imports: [IssueListComponent],
    providers: [
      IssueService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SignalRService, useValue: mockSignalRService },
    ],
  });

  const fixture = TestBed.createComponent(IssueListComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

describe('IssueListComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle 1: component creates and renders heading
  it('should create the component', () => {
    // Arrange / Act
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush([]);

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the "Tracked Issues" heading', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush([]);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('.issue-list__heading');
    expect(heading?.textContent?.trim()).toBe('Tracked Issues');
  });

  // Cycle 2: calls loadIssues on init
  it('should call loadIssues on initialization', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();

    // Assert — the HTTP call proves loadIssues was called
    const req = httpMock.expectOne('/api/issues');
    req.flush([]);
  });

  // Cycle 3: renders fd-issue-card for each issue
  it('should render fd-issue-card for each sorted issue', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush([mockSummary]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const cards = el.querySelectorAll('fd-issue-card');
    expect(cards.length).toBe(1);
  });

  it('should render multiple cards when multiple issues are loaded', () => {
    // Arrange
    const second: IssueSummary = { ...mockSummary, id: 'def456', issueNumber: 43, title: 'Fix bug' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush([mockSummary, second]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const cards = el.querySelectorAll('fd-issue-card');
    expect(cards.length).toBe(2);
  });

  // Cycle 4: shows empty state when no issues
  it('should render fd-empty-state when there are no issues', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush([]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const emptyState = el.querySelector('fd-empty-state');
    expect(emptyState).toBeTruthy();
  });

  it('should not render fd-empty-state when issues exist', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush([mockSummary]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const emptyState = el.querySelector('fd-empty-state');
    expect(emptyState).toBeFalsy();
  });

  // Cycle 4b: detail wrapper has stable id for aria-controls
  it('should give the detail wrapper an id matching the issue id', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush([mockSummary]);
    fixture.detectChanges();

    // Act - expand the card
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    card.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues/abc123').flush({
      ...mockSummary,
      body: null,
      author: 'dev',
      labels: [],
      stateDetails: {
        workerRunId: null,
        branchName: null,
        pullRequestUrl: null,
        feedbackCutoffAt: null,
        failureReason: null,
        failedAt: null,
        completedAt: null,
        blockedBy: null,
      },
    });
    fixture.detectChanges();

    // Assert
    const wrapper = el.querySelector('.issue-list__detail-wrapper') as HTMLElement;
    expect(wrapper?.getAttribute('id')).toBe('detail-abc123');
  });

  // Cycle 5: renders fd-connection-indicator
  it('should render fd-connection-indicator in the header', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush([]);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const indicator = el.querySelector('fd-connection-indicator');
    expect(indicator).toBeTruthy();
  });

  // Cycle 6: expand/collapse wiring - fd-issue-detail appears when card is expanded
  it('should show fd-issue-detail for the expanded issue after card toggle', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush([mockSummary]);
    fixture.detectChanges();

    // Act - click the card to toggle expand
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    card.click();
    fixture.detectChanges();

    // Flush the detail request
    httpMock.expectOne('/api/issues/abc123').flush({
      ...mockSummary,
      body: 'Body text',
      author: 'dev',
      labels: [],
      stateDetails: {
        workerRunId: null,
        branchName: null,
        pullRequestUrl: null,
        feedbackCutoffAt: null,
        failureReason: null,
        failedAt: null,
        completedAt: null,
        blockedBy: null,
      },
    });
    fixture.detectChanges();

    // Assert
    const detail = el.querySelector('fd-issue-detail');
    expect(detail).toBeTruthy();
  });
});
