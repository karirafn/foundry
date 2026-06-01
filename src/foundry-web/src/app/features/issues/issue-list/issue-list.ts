import { Component, OnInit, inject } from '@angular/core';
import { IssueService } from '../issue.service';
import { SignalRService } from '../../../core/services/signalr.service';
import { IssueCardComponent } from '../issue-card/issue-card';
import { IssueDetailComponent } from '../issue-detail/issue-detail';
import { ConnectionIndicatorComponent } from '../../../shared/components/connection-indicator/connection-indicator';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state';

@Component({
  selector: 'fd-issue-list',
  standalone: true,
  imports: [
    IssueCardComponent,
    IssueDetailComponent,
    ConnectionIndicatorComponent,
    EmptyStateComponent,
  ],
  template: `
    <div class="issue-list">
      <header class="issue-list__header">
        <h1 class="issue-list__heading">Tracked Issues</h1>
        <fd-connection-indicator [status]="signalR.connectionStatus()" />
      </header>

      @if (issueService.loadError()) {
        <div class="issue-list__error" role="alert">
          <span class="issue-list__error-message">Failed to load issues</span>
          <button
            class="issue-list__error-retry"
            type="button"
            (click)="issueService.loadIssues()"
          >Retry</button>
        </div>
      }

      @if (!issueService.initialLoading() && issueService.isEmpty() && !issueService.loadError()) {
        <fd-empty-state />
      }

      <div class="issue-list__grid">
        @for (issue of issueService.sortedIssues(); track issue.id) {
          <div class="issue-list__item">
            <fd-issue-card
              [issue]="issue"
              [expanded]="issueService.expandedIssueId() === issue.id"
              (toggle)="issueService.toggleExpand(issue.id)"
            />

            <div
              class="issue-list__detail-wrapper"
              [id]="'detail-' + issue.id"
              [hidden]="issueService.expandedIssueId() !== issue.id"
            >
              @if (issueService.expandedIssueId() === issue.id) {
                <fd-issue-detail
                  [detail]="issueService.issueDetail()"
                  [loading]="issueService.detailLoading()"
                  [error]="issueService.detailError()"
                  (retry)="issueService.loadDetail(issueService.expandedIssueId()!)"
                />
              }
            </div>
          </div>
        }
      </div>
    </div>
  `,
  styleUrl: './issue-list.scss',
})
export class IssueListComponent implements OnInit {
  protected readonly issueService = inject(IssueService);
  protected readonly signalR = inject(SignalRService);

  ngOnInit(): void {
    this.issueService.loadIssues();
  }
}
