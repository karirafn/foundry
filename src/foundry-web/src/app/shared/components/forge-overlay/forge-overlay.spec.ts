import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { Subject } from 'rxjs';
import { ForgeOverlayComponent } from './forge-overlay';
import { SettingsService } from '../../../features/settings/settings.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { ImageBuildStatus } from '../../../features/settings/settings.model';

function createMockSettingsService(overrides: {
  isColdBuildBlocking?: boolean;
  imageBuildStatus?: ImageBuildStatus;
  imageBuildLogTail?: string | null;
} = {}) {
  const isColdBuildBlockingSignal = signal(overrides.isColdBuildBlocking ?? true);
  const imageBuildStatusSignal = signal<ImageBuildStatus>(overrides.imageBuildStatus ?? 'Idle');
  const imageBuildLogTailSignal = signal<string | null>(overrides.imageBuildLogTail ?? null);
  const retryImageBuild = vi.fn();
  const loadSettings = vi.fn();

  return {
    isColdBuildBlocking: isColdBuildBlockingSignal.asReadonly(),
    imageBuildStatus: imageBuildStatusSignal.asReadonly(),
    imageBuildLogTail: imageBuildLogTailSignal.asReadonly(),
    retryImageBuild,
    loadSettings,
    _isColdBuildBlockingSignal: isColdBuildBlockingSignal,
    _imageBuildStatusSignal: imageBuildStatusSignal,
    _imageBuildLogTailSignal: imageBuildLogTailSignal,
  };
}

function createMockSignalRService() {
  return {
    reconnected: new Subject<void>(),
  };
}

interface SetupOptions {
  isColdBuildBlocking?: boolean;
  imageBuildStatus?: ImageBuildStatus;
  imageBuildLogTail?: string | null;
}

function setup(options: SetupOptions = {}) {
  const mockSettings = createMockSettingsService({
    isColdBuildBlocking: options.isColdBuildBlocking ?? true,
    imageBuildStatus: options.imageBuildStatus ?? 'Idle',
    imageBuildLogTail: options.imageBuildLogTail ?? null,
  });
  const mockSignalR = createMockSignalRService();

  TestBed.configureTestingModule({
    imports: [ForgeOverlayComponent],
    providers: [
      { provide: SettingsService, useValue: mockSettings },
      { provide: SystemSignalRService, useValue: mockSignalR },
    ],
  });

  const fixture = TestBed.createComponent(ForgeOverlayComponent);
  fixture.detectChanges();

  return { fixture, mockSettings, mockSignalR };
}

describe('ForgeOverlayComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  // Tracer bullet: overlay is visible when isColdBuildBlocking is true
  it('should show the overlay when isColdBuildBlocking is true', () => {
    // Arrange
    const { fixture } = setup({ isColdBuildBlocking: true });

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.forge-overlay')).not.toBeNull();
  });

  // Overlay is hidden when isColdBuildBlocking is false
  it('should hide the overlay when isColdBuildBlocking is false', () => {
    // Arrange
    const { fixture } = setup({ isColdBuildBlocking: false });

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.forge-overlay')).toBeNull();
  });

  // Overlay dismisses when isColdBuildBlocking transitions to false
  it('should dismiss the overlay when isColdBuildBlocking changes to false', () => {
    // Arrange
    const { fixture, mockSettings } = setup({ isColdBuildBlocking: true });
    expect((fixture.nativeElement as HTMLElement).querySelector('.forge-overlay')).not.toBeNull();

    // Act
    mockSettings._isColdBuildBlockingSignal.set(false);
    fixture.detectChanges();

    // Assert
    expect((fixture.nativeElement as HTMLElement).querySelector('.forge-overlay')).toBeNull();
  });

  // State: Idle shows "Starting…" text
  it('should show "Starting…" text when imageBuildStatus is Idle', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Idle' });

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.textContent).toContain('Starting…');
  });

  // State: Building shows "Building worker image…" text
  it('should show "Building worker image…" text when imageBuildStatus is Building', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Building' });

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.textContent).toContain('Building worker image…');
  });

  // State: Failed shows error heading
  it('should show "Worker image build failed" heading when imageBuildStatus is Failed', () => {
    // Arrange
    const { fixture } = setup({
      imageBuildStatus: 'Failed',
      imageBuildLogTail: 'Step 2/5 FAILED',
    });

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.textContent).toContain('Worker image build failed');
  });

  // State: Failed shows error tail
  it('should show the error log tail when imageBuildStatus is Failed', () => {
    // Arrange
    const { fixture } = setup({
      imageBuildStatus: 'Failed',
      imageBuildLogTail: 'Step 2/5 FAILED',
    });

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.textContent).toContain('Step 2/5 FAILED');
  });

  // State: Failed shows Retry button
  it('should render a Retry button when imageBuildStatus is Failed', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Failed' });

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const retryBtn = el.querySelector('button');
    expect(retryBtn).not.toBeNull();
    expect(retryBtn?.textContent?.trim()).toBe('Retry');
  });

  // Retry button calls retryImageBuild
  it('should call retryImageBuild when Retry button is clicked', () => {
    // Arrange
    const { fixture, mockSettings } = setup({ imageBuildStatus: 'Failed' });
    const retryBtn = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;

    // Act
    retryBtn.click();

    // Assert
    expect(mockSettings.retryImageBuild).toHaveBeenCalledOnce();
  });

  // No Retry button when Building
  it('should not render a Retry button when imageBuildStatus is Building', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Building' });

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('button')).toBeNull();
  });

  // Accessibility: role="alertdialog" on the surface
  it('should have role="alertdialog" on the overlay surface', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const surface = el.querySelector('[role="alertdialog"]');
    expect(surface).not.toBeNull();
  });

  // Accessibility: aria-modal="true" on the surface
  it('should have aria-modal="true" on the overlay surface', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const surface = el.querySelector('[aria-modal="true"]');
    expect(surface).not.toBeNull();
  });

  // Accessibility: persistent aria-live status region
  it('should have a persistent role="status" aria-live region', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const liveRegion = el.querySelector('[role="status"][aria-live="polite"]');
    expect(liveRegion).not.toBeNull();
  });

  // Live region text for Idle state
  it('should announce "Starting…" in the live region when Idle', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Idle' });

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const liveRegion = el.querySelector('[role="status"]');

    // Assert
    expect(liveRegion?.textContent).toContain('Starting…');
  });

  // Live region text for Building state
  it('should announce "Building worker image…" in the live region when Building', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Building' });

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const liveRegion = el.querySelector('[role="status"]');

    // Assert
    expect(liveRegion?.textContent).toContain('Building worker image…');
  });

  // Live region text for Failed state
  it('should announce "Worker image build failed" in the live region when Failed', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Failed' });

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const liveRegion = el.querySelector('[role="status"]');

    // Assert
    expect(liveRegion?.textContent).toContain('Worker image build failed');
  });

  // Forge scene is always present while blocking (state-invariant)
  it('should always render the forge scene element while blocking', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Building' });

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.forge-overlay__scene')).not.toBeNull();
  });

  // Accessibility: dialog has stable aria-labelledby pointing at persistent title (F4)
  it('should have aria-labelledby pointing at the persistent title element', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Failed' });

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const surface = el.querySelector('[role="alertdialog"]') as HTMLElement;
    const labelId = surface?.getAttribute('aria-labelledby');
    const labelEl = labelId ? el.querySelector(`#${labelId}`) : null;

    // Assert
    expect(labelEl).not.toBeNull();
    expect(labelEl?.textContent?.trim()).toBe('Worker Image Build');
  });

  // Accessibility: dialog accessible name does not include "Retry" (F4)
  it('should not include "Retry" in the dialog accessible name', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Failed' });

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const surface = el.querySelector('[role="alertdialog"]') as HTMLElement;
    const labelId = surface?.getAttribute('aria-labelledby');
    const labelEl = labelId ? el.querySelector(`#${labelId}`) : null;

    // Assert
    expect(labelEl?.textContent?.trim()).not.toContain('Retry');
  });

  // Accessibility: dialog has aria-describedby pointing at status-detail region (F7)
  it('should have aria-describedby pointing at the status-detail element', () => {
    // Arrange
    const { fixture } = setup({ imageBuildStatus: 'Building' });

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const surface = el.querySelector('[role="alertdialog"]') as HTMLElement;
    const descId = surface?.getAttribute('aria-describedby');
    const descEl = descId ? el.querySelector(`#${descId}`) : null;

    // Assert
    expect(descEl).not.toBeNull();
    expect(descEl?.textContent).toContain('Building worker image…');
  });

  // Reconnect triggers loadSettings
  it('should call loadSettings when SignalR reconnects', () => {
    // Arrange
    const { mockSettings, mockSignalR } = setup();

    // Act
    mockSignalR.reconnected.next();

    // Assert
    expect(mockSettings.loadSettings).toHaveBeenCalledOnce();
  });

  // Reconnect + isColdBuildBlocking false dismisses the overlay
  it('should dismiss overlay after reconnected fires and isColdBuildBlocking becomes false', () => {
    // Arrange
    const { fixture, mockSettings, mockSignalR } = setup({ isColdBuildBlocking: true });
    expect((fixture.nativeElement as HTMLElement).querySelector('.forge-overlay')).not.toBeNull();

    // Act — simulate reconnect followed by a settings reload that now has a usable image
    mockSignalR.reconnected.next();
    mockSettings._isColdBuildBlockingSignal.set(false);
    fixture.detectChanges();

    // Assert — overlay is dismissed
    expect((fixture.nativeElement as HTMLElement).querySelector('.forge-overlay')).toBeNull();
  });

  // State transition: Building -> Failed renders Failed branch
  it('should render the Failed branch when imageBuildStatus transitions from Building to Failed', () => {
    // Arrange — start in Building state
    const { fixture, mockSettings } = setup({
      imageBuildStatus: 'Building',
      imageBuildLogTail: null,
    });
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Building worker image…');
    expect(el.querySelector('button')).toBeNull();

    // Act — transition to Failed with an error log tail
    mockSettings._imageBuildStatusSignal.set('Failed');
    mockSettings._imageBuildLogTailSignal.set('Step 3/5 FAILED');
    fixture.detectChanges();

    // Assert — Failed branch is rendered with error tail and Retry button
    expect(el.textContent).toContain('Worker image build failed');
    expect(el.textContent).toContain('Step 3/5 FAILED');
    const retryBtn = el.querySelector('button');
    expect(retryBtn).not.toBeNull();
    expect(retryBtn?.textContent?.trim()).toBe('Retry');
  });

  // F2: Retry button receives focus after Building -> Failed transition
  it('should move focus to the Retry button when status transitions to Failed while blocking', async () => {
    // Arrange — start in Building state
    const { fixture, mockSettings } = setup({ imageBuildStatus: 'Building' });

    // Act — transition to Failed so afterRenderEffect queues a focus call
    mockSettings._imageBuildStatusSignal.set('Failed');
    fixture.detectChanges();
    TestBed.flushEffects();

    // Assert — Retry button exists and is focused
    const retryBtn = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;
    expect(retryBtn).not.toBeNull();
    expect(document.activeElement).toBe(retryBtn);
  });
});
