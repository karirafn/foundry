import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { Subject } from 'rxjs';
import { ForgeOverlayComponent } from './forge-overlay';
import { SettingsService } from '../../../features/settings/settings.service';
import { AccountService } from '../../../features/settings/accounts/account.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { ImageBuildStatus } from '../../../features/settings/settings.model';

function createMockSettingsService(overrides: {
  hasUsableImage?: boolean;
  imageBuildStatus?: ImageBuildStatus;
  imageBuildLogTail?: string | null;
} = {}) {
  const hasUsableImageSignal = signal(overrides.hasUsableImage ?? false);
  const imageBuildStatusSignal = signal<ImageBuildStatus>(overrides.imageBuildStatus ?? 'Idle');
  const imageBuildLogTailSignal = signal<string | null>(overrides.imageBuildLogTail ?? null);
  const retryImageBuild = vi.fn();
  const loadSettings = vi.fn();

  return {
    hasUsableImage: hasUsableImageSignal.asReadonly(),
    imageBuildStatus: imageBuildStatusSignal.asReadonly(),
    imageBuildLogTail: imageBuildLogTailSignal.asReadonly(),
    retryImageBuild,
    loadSettings,
    _hasUsableImageSignal: hasUsableImageSignal,
    _imageBuildStatusSignal: imageBuildStatusSignal,
    _imageBuildLogTailSignal: imageBuildLogTailSignal,
  };
}

function createMockAccountService(accounts: unknown[] = []) {
  const accountsSignal = signal(accounts);
  return {
    accounts: accountsSignal.asReadonly(),
    _accountsSignal: accountsSignal,
  };
}

function createMockSignalRService() {
  return {
    reconnected: new Subject<void>(),
  };
}

interface SetupOptions {
  hasUsableImage?: boolean;
  imageBuildStatus?: ImageBuildStatus;
  imageBuildLogTail?: string | null;
  accounts?: unknown[];
}

function setup(options: SetupOptions = {}) {
  const mockSettings = createMockSettingsService({
    hasUsableImage: options.hasUsableImage ?? false,
    imageBuildStatus: options.imageBuildStatus ?? 'Idle',
    imageBuildLogTail: options.imageBuildLogTail ?? null,
  });
  const mockAccounts = createMockAccountService(options.accounts ?? [{ id: '1' }]);
  const mockSignalR = createMockSignalRService();

  TestBed.configureTestingModule({
    imports: [ForgeOverlayComponent],
    providers: [
      { provide: SettingsService, useValue: mockSettings },
      { provide: AccountService, useValue: mockAccounts },
      { provide: SystemSignalRService, useValue: mockSignalR },
    ],
  });

  const fixture = TestBed.createComponent(ForgeOverlayComponent);
  fixture.detectChanges();

  return { fixture, mockSettings, mockAccounts, mockSignalR };
}

describe('ForgeOverlayComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  // Tracer bullet: overlay is visible when setup complete and no usable image
  it('should show the overlay when setup is complete and hasUsableImage is false', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.forge-overlay')).not.toBeNull();
  });

  // Overlay is hidden when hasUsableImage becomes true
  it('should hide the overlay when hasUsableImage is true', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: true });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.forge-overlay')).toBeNull();
  });

  // Overlay is hidden when no accounts (setup not complete)
  it('should hide the overlay when there are no accounts (setup incomplete)', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [], hasUsableImage: false });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.forge-overlay')).toBeNull();
  });

  // Overlay dismisses when hasUsableImage transitions to true
  it('should dismiss the overlay when hasUsableImage changes to true', () => {
    // Arrange
    const { fixture, mockSettings } = setup({ accounts: [{ id: '1' }], hasUsableImage: false });
    expect((fixture.nativeElement as HTMLElement).querySelector('.forge-overlay')).not.toBeNull();

    // Act
    mockSettings._hasUsableImageSignal.set(true);
    fixture.detectChanges();

    // Assert
    expect((fixture.nativeElement as HTMLElement).querySelector('.forge-overlay')).toBeNull();
  });

  // State: Idle shows "Starting…" text
  it('should show "Starting…" text when imageBuildStatus is Idle', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false, imageBuildStatus: 'Idle' });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.textContent).toContain('Starting…');
  });

  // State: Building shows "Building worker image…" text
  it('should show "Building worker image…" text when imageBuildStatus is Building', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false, imageBuildStatus: 'Building' });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.textContent).toContain('Building worker image…');
  });

  // State: Failed shows error heading
  it('should show "Worker image build failed" heading when imageBuildStatus is Failed', () => {
    // Arrange / Act
    const { fixture } = setup({
      accounts: [{ id: '1' }],
      hasUsableImage: false,
      imageBuildStatus: 'Failed',
      imageBuildLogTail: 'Step 2/5 FAILED',
    });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.textContent).toContain('Worker image build failed');
  });

  // State: Failed shows error tail
  it('should show the error log tail when imageBuildStatus is Failed', () => {
    // Arrange / Act
    const { fixture } = setup({
      accounts: [{ id: '1' }],
      hasUsableImage: false,
      imageBuildStatus: 'Failed',
      imageBuildLogTail: 'Step 2/5 FAILED',
    });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.textContent).toContain('Step 2/5 FAILED');
  });

  // State: Failed shows Retry button
  it('should render a Retry button when imageBuildStatus is Failed', () => {
    // Arrange / Act
    const { fixture } = setup({
      accounts: [{ id: '1' }],
      hasUsableImage: false,
      imageBuildStatus: 'Failed',
    });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const retryBtn = el.querySelector('button');
    expect(retryBtn).not.toBeNull();
    expect(retryBtn?.textContent?.trim()).toBe('Retry');
  });

  // Retry button calls retryImageBuild
  it('should call retryImageBuild when Retry button is clicked', () => {
    // Arrange
    const { fixture, mockSettings } = setup({
      accounts: [{ id: '1' }],
      hasUsableImage: false,
      imageBuildStatus: 'Failed',
    });
    const retryBtn = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;

    // Act
    retryBtn.click();

    // Assert
    expect(mockSettings.retryImageBuild).toHaveBeenCalledOnce();
  });

  // No Retry button when Building
  it('should not render a Retry button when imageBuildStatus is Building', () => {
    // Arrange / Act
    const { fixture } = setup({
      accounts: [{ id: '1' }],
      hasUsableImage: false,
      imageBuildStatus: 'Building',
    });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('button')).toBeNull();
  });

  // Accessibility: role="alertdialog" on the surface
  it('should have role="alertdialog" on the overlay surface', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const surface = el.querySelector('[role="alertdialog"]');
    expect(surface).not.toBeNull();
  });

  // Accessibility: aria-modal="true" on the surface
  it('should have aria-modal="true" on the overlay surface', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const surface = el.querySelector('[aria-modal="true"]');
    expect(surface).not.toBeNull();
  });

  // Accessibility: persistent aria-live status region
  it('should have a persistent role="status" aria-live region', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const liveRegion = el.querySelector('[role="status"][aria-live="polite"]');
    expect(liveRegion).not.toBeNull();
  });

  // Live region text for Idle state
  it('should announce "Starting…" in the live region when Idle', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false, imageBuildStatus: 'Idle' });
    const el = fixture.nativeElement as HTMLElement;
    const liveRegion = el.querySelector('[role="status"]');

    // Assert
    expect(liveRegion?.textContent).toContain('Starting…');
  });

  // Live region text for Building state
  it('should announce "Building worker image…" in the live region when Building', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false, imageBuildStatus: 'Building' });
    const el = fixture.nativeElement as HTMLElement;
    const liveRegion = el.querySelector('[role="status"]');

    // Assert
    expect(liveRegion?.textContent).toContain('Building worker image…');
  });

  // Live region text for Failed state
  it('should announce "Worker image build failed" in the live region when Failed', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false, imageBuildStatus: 'Failed' });
    const el = fixture.nativeElement as HTMLElement;
    const liveRegion = el.querySelector('[role="status"]');

    // Assert
    expect(liveRegion?.textContent).toContain('Worker image build failed');
  });

  // Forge scene is always present while blocking (state-invariant)
  it('should always render the forge scene element while blocking', () => {
    // Arrange / Act
    const { fixture } = setup({ accounts: [{ id: '1' }], hasUsableImage: false, imageBuildStatus: 'Building' });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.forge-overlay__scene')).not.toBeNull();
  });

  // Reconnect triggers loadSettings
  it('should call loadSettings when SignalR reconnects', () => {
    // Arrange
    const { mockSettings, mockSignalR } = setup({ accounts: [{ id: '1' }], hasUsableImage: false });

    // Act
    mockSignalR.reconnected.next();

    // Assert
    expect(mockSettings.loadSettings).toHaveBeenCalledOnce();
  });
});
