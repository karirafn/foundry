import { TestBed } from '@angular/core/testing';
import { ConnectionIndicatorComponent } from './connection-indicator';
import { ConnectionStatus } from '../../../core/services/issue-signalr.service';

function createComponent(status: ConnectionStatus) {
  TestBed.configureTestingModule({
    imports: [ConnectionIndicatorComponent],
  });
  const fixture = TestBed.createComponent(ConnectionIndicatorComponent);
  fixture.componentRef.setInput('status', status);
  fixture.detectChanges();
  return fixture;
}

describe('ConnectionIndicatorComponent', () => {
  // Cycle 1: component creates with role and aria attributes
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createComponent('connected');

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should have role="status" without redundant aria-live', () => {
    // Arrange / Act
    const fixture = createComponent('connected');
    const el = fixture.nativeElement as HTMLElement;
    const host = el.querySelector('.connection-indicator') as HTMLElement;

    // Assert
    expect(host?.getAttribute('role')).toBe('status');
    expect(host?.getAttribute('aria-live')).toBeNull();
  });

  // Cycle 2: label text for each connection status
  it('should display "Connected" label when status is connected', () => {
    // Arrange / Act
    const fixture = createComponent('connected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.connection-indicator__label')?.textContent?.trim()).toBe('Connected');
  });

  it('should display "Reconnecting..." label when status is reconnecting', () => {
    // Arrange / Act
    const fixture = createComponent('reconnecting');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.connection-indicator__label')?.textContent?.trim()).toBe('Reconnecting...');
  });

  it('should display "Disconnected" label when status is disconnected', () => {
    // Arrange / Act
    const fixture = createComponent('disconnected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.connection-indicator__label')?.textContent?.trim()).toBe('Disconnected');
  });

  // Cycle 3: CSS class on dot reflects current status
  it('should apply dot--connected class when status is connected', () => {
    // Arrange / Act
    const fixture = createComponent('connected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.connection-indicator__dot')?.classList.contains('dot--connected')).toBe(true);
  });

  it('should apply dot--reconnecting class when status is reconnecting', () => {
    // Arrange / Act
    const fixture = createComponent('reconnecting');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.connection-indicator__dot')?.classList.contains('dot--reconnecting')).toBe(true);
  });

  it('should apply dot--disconnected class when status is disconnected', () => {
    // Arrange / Act
    const fixture = createComponent('disconnected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.connection-indicator__dot')?.classList.contains('dot--disconnected')).toBe(true);
  });

  // Cycle 4: aria-label is descriptive
  it('should set descriptive aria-label for connected status', () => {
    // Arrange / Act
    const fixture = createComponent('connected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const host = el.querySelector('.connection-indicator') as HTMLElement;
    expect(host?.getAttribute('aria-label')).toBe('Connection status: Connected');
  });

  it('should set descriptive aria-label for reconnecting status', () => {
    // Arrange / Act
    const fixture = createComponent('reconnecting');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const host = el.querySelector('.connection-indicator') as HTMLElement;
    expect(host?.getAttribute('aria-label')).toBe('Connection status: Reconnecting...');
  });

  it('should set title attribute with full description', () => {
    // Arrange / Act
    const fixture = createComponent('disconnected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const host = el.querySelector('.connection-indicator') as HTMLElement;
    expect(host?.getAttribute('title')).toBe('Connection status: Disconnected');
  });
});
