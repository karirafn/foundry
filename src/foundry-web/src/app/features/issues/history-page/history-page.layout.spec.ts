import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideLocationMocks } from '@angular/common/testing';
import { provideRouter } from '@angular/router';
import { HistoryPageComponent } from './history-page';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { provideFakeHubs } from '../../../core/testing/fake-hub-transport';

@Component({
  selector: 'fd-history-page-layout-host',
  imports: [HistoryPageComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-history-page /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
}

async function render(width: number): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      provideLocationMocks(),
      {
        provide: IssueSignalRService,
        useValue: { on: () => {}, onReconnected: () => {}, connectionStatus: () => 'connected' },
      },
      provideFakeHubs(),
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('history page layout', () => {
  it.each([390, 1440])('aligns the header with the stats bar at %ipx', async (width) => {
    // Arrange
    const host = await render(width);

    // Act
    const header = host.querySelector('.history-page__header')!.getBoundingClientRect();
    const stats = host.querySelector('.history-page__stats')!.getBoundingClientRect();

    // Assert
    expect(header.width).toBeGreaterThan(0);
    expect(header.left).toBeCloseTo(stats.left, 0);
    expect(header.width).toBeCloseTo(stats.width, 0);
  });
});
