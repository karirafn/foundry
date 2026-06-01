import { TestBed } from '@angular/core/testing';
import { IssueDetailComponent } from './issue-detail';
import { IssueDetail } from '../issue.model';

const mockDetail: IssueDetail = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'completed',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
  body: 'We need a dark mode for the dashboard.',
  author: 'dev',
  labels: ['enhancement', 'ui'],
  stateDetails: {
    workerRunId: null,
    branchName: 'feat/dark-mode',
    pullRequestUrl: 'https://github.com/owner/repo/pull/99',
    feedbackCutoffAt: null,
    failureReason: null,
    failedAt: null,
    completedAt: '2026-02-01T12:00:00Z',
    blockedBy: null,
  },
};

function createComponent(detail: IssueDetail | null, loading = false, error: string | null = null) {
  TestBed.configureTestingModule({
    imports: [IssueDetailComponent],
  });
  const fixture = TestBed.createComponent(IssueDetailComponent);
  fixture.componentRef.setInput('detail', detail);
  fixture.componentRef.setInput('loading', loading);
  fixture.componentRef.setInput('error', error);
  fixture.detectChanges();
  return fixture;
}

describe('IssueDetailComponent', () => {
  // Cycle 1: component creates
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createComponent(null, false);

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render with role="region" when detail is provided', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const region = el.querySelector('[role="region"]');
    expect(region).toBeTruthy();
  });

  // Cycle 2: loading skeleton
  it('should show shimmer skeleton when loading is true', () => {
    // Arrange / Act
    const fixture = createComponent(null, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeTruthy();
  });

  it('should mark skeleton container as aria-busy when loading', () => {
    // Arrange / Act
    const fixture = createComponent(null, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const skeleton = el.querySelector('[aria-busy="true"]');
    expect(skeleton).toBeTruthy();
  });

  it('should render three shimmer bars in the skeleton', () => {
    // Arrange / Act
    const fixture = createComponent(null, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bars = el.querySelectorAll('.issue-detail__shimmer-bar');
    expect(bars.length).toBe(3);
  });

  it('should not show skeleton when not loading', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail, false);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeFalsy();
  });

  // Cycle 3: body content
  it('should display the issue body text', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const body = el.querySelector('.issue-detail__body');
    expect(body?.textContent?.trim()).toContain('We need a dark mode');
  });

  // Cycle 4: labels
  it('should render each label as a pill', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pills = el.querySelectorAll('.issue-detail__label-pill');
    expect(pills.length).toBe(2);
    const texts = Array.from(pills).map((p) => p.textContent?.trim());
    expect(texts).toContain('enhancement');
    expect(texts).toContain('ui');
  });

  // Cycle 5: state detail fields - PR link
  it('should render a PR link when pullRequestUrl is present', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/pull/99');
    expect(link?.getAttribute('target')).toBe('_blank');
  });

  it('should set aria-label on PR link to include issue number', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link?.getAttribute('aria-label')).toBe('Open pull request for issue #42');
  });

  it('should not render a PR link when pullRequestUrl is not https', () => {
    // Arrange
    const detailHttpPr: IssueDetail = {
      ...mockDetail,
      stateDetails: { ...mockDetail.stateDetails, pullRequestUrl: 'http://github.com/owner/repo/pull/99' },
    };

    // Act
    const fixture = createComponent(detailHttpPr);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link');
    expect(link).toBeFalsy();
  });

  it('should not render a PR link when pullRequestUrl is null', () => {
    // Arrange
    const detailNoPr: IssueDetail = {
      ...mockDetail,
      stateDetails: { ...mockDetail.stateDetails, pullRequestUrl: null },
    };

    // Act
    const fixture = createComponent(detailNoPr);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link');
    expect(link).toBeFalsy();
  });

  // Cycle 6: state detail fields - branchName
  it('should display the branch name when present', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const branch = el.querySelector('.issue-detail__branch');
    expect(branch?.textContent?.trim()).toBe('feat/dark-mode');
  });

  it('should not render branch row when branchName is null', () => {
    // Arrange
    const detailNoBranch: IssueDetail = {
      ...mockDetail,
      stateDetails: { ...mockDetail.stateDetails, branchName: null },
    };

    // Act
    const fixture = createComponent(detailNoBranch);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const branch = el.querySelector('.issue-detail__branch');
    expect(branch).toBeFalsy();
  });

  // Cycle 7: aria-label includes issue number
  it('should set aria-label referencing the issue number', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const region = el.querySelector('[role="region"]') as HTMLElement;
    expect(region?.getAttribute('aria-label')).toBe('Issue details for #42');
  });

  // Cycle 8: error state
  it('should show error message when error input is truthy', () => {
    // Arrange / Act
    const fixture = createComponent(null, false, 'Http failure');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const errorEl = el.querySelector('.issue-detail__error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.textContent).toContain('Failed to load details');
  });

  it('should not show detail content when error is present', () => {
    // Arrange / Act
    const fixture = createComponent(null, false, 'Http failure');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const content = el.querySelector('.issue-detail__content');
    expect(content).toBeFalsy();
  });

  it('should not show skeleton when error is present', () => {
    // Arrange / Act
    const fixture = createComponent(null, true, 'Http failure');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeFalsy();
  });

  it('should not show error block when error is null', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail, false, null);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const errorEl = el.querySelector('.issue-detail__error');
    expect(errorEl).toBeFalsy();
  });
});
