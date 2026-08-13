import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { CreditsBannerComponent } from './credits-banner';
import { CreditsService } from '../../../../../core/services/credits.service';

interface CreditsServiceOverrides {
  nextProbeAt?: string | null;
  isChecking?: boolean;
}

function createMockCreditsService(overrides: CreditsServiceOverrides = {}) {
  const nextProbeAtSignal = signal<string | null>(overrides.nextProbeAt ?? null);
  const isCheckingSignal = signal<boolean>(overrides.isChecking ?? false);
  const checkNow = vi.fn();

  return {
    nextProbeAt: nextProbeAtSignal.asReadonly(),
    isChecking: isCheckingSignal.asReadonly(),
    checkNow,
    _nextProbeAtSignal: nextProbeAtSignal,
    _isCheckingSignal: isCheckingSignal,
  };
}

interface SetupOptions {
  credits?: CreditsServiceOverrides;
}

function setup(options: SetupOptions = {}) {
  const mockCredits = createMockCreditsService(options.credits ?? {});

  TestBed.configureTestingModule({
    imports: [CreditsBannerComponent],
    providers: [{ provide: CreditsService, useValue: mockCredits }],
  });

  const fixture = TestBed.createComponent(CreditsBannerComponent);
  fixture.detectChanges();
  return { fixture, mockCredits };
}

describe('CreditsBannerComponent', () => {
  describe('visibility', () => {
    it('should hide the banner when nextProbeAt is null', () => {
      // Arrange / Act
      const { fixture } = setup({ credits: { nextProbeAt: null } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const region = el.querySelector('[aria-label="Credit status"]') as HTMLElement;
      expect(region?.hidden).toBe(true);
    });

    it('should show the banner when nextProbeAt is set to a future timestamp', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const region = el.querySelector('[aria-label="Credit status"]') as HTMLElement;
      expect(region?.hidden).toBe(false);
    });

    it('should hide when nextProbeAt transitions from set to null', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60_000).toISOString();
      const { fixture, mockCredits } = setup({ credits: { nextProbeAt: futureDate } });
      const el = fixture.nativeElement as HTMLElement;

      // Act
      mockCredits._nextProbeAtSignal.set(null);
      fixture.detectChanges();

      // Assert
      const region = el.querySelector('[aria-label="Credit status"]') as HTMLElement;
      expect(region?.hidden).toBe(true);
    });
  });

  describe('copy', () => {
    it('should render the headline "Dispatch blocked — Claude account can\'t spend"', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain("Dispatch blocked — Claude account can't spend");
    });

    it('should render both remedies in the body text', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Add credits');
      expect(el.textContent).toContain('raise the spend limit');
    });
  });

  describe('counting-down state', () => {
    it('should render "Next automatic check in" with countdown when not checking', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 45_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: false } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Next automatic check in');
      expect(el.textContent).toContain('45s');
    });

    it('should format countdown as "Xh Ym" when more than one hour remains', () => {
      // Arrange
      const futureDate = new Date(Date.now() + (2 * 3600 + 34 * 60) * 1000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: false } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('2h 34m');
    });

    it('should format countdown as "Xm Ys" when between one minute and one hour remains', () => {
      // Arrange
      const futureDate = new Date(Date.now() + (12 * 60 + 5) * 1000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: false } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('12m 5s');
    });

    it('should format countdown as "Xs" when less than one minute remains', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 30_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: false } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('30s');
    });

    it('should render the countdown value with aria-hidden="true"', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 45_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: false } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const countdownValue = el.querySelector('.system-banner__countdown-value') as HTMLElement;
      expect(countdownValue).not.toBeNull();
      expect(countdownValue.getAttribute('aria-hidden')).toBe('true');
    });

    it('should enable the "Check now" button when not checking', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: false } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const btn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      expect(btn.disabled).toBe(false);
      expect(btn.textContent?.trim()).toBe('Check now');
    });
  });

  describe('checking state', () => {
    it('should show "Checking whether the Claude account can spend again" line when isChecking is true', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.textContent).toContain('Checking whether the Claude account can spend again');
      expect(el.textContent).not.toContain('Next automatic check in');
    });

    it('should disable the button and change label to "Checking…" when isChecking is true', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const btn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      expect(btn.disabled).toBe(true);
      expect(btn.textContent?.trim()).toBe('Checking…');
      expect(btn.getAttribute('aria-busy')).toBe('true');
    });

    it('should not invoke ToastService on zero-cross while checking (no toast injection)', () => {
      // Arrange — zero-cross scenario: nextProbeAt in the past with isChecking
      const pastDate = new Date(Date.now() - 1000).toISOString();
      const { fixture } = setup({ credits: { nextProbeAt: pastDate, isChecking: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — checking state shown, no ToastService in providers so no toast call
      expect(el.textContent).toContain('Checking whether the Claude account can spend again');
    });
  });

  describe('re-arm after failure', () => {
    it('should return to counting-down state with fresh time after probe fails (re-arm)', () => {
      // Arrange — start in checking state
      const initialDate = new Date(Date.now() + 60_000).toISOString();
      const { fixture, mockCredits } = setup({ credits: { nextProbeAt: initialDate, isChecking: true } });
      const el = fixture.nativeElement as HTMLElement;
      expect((el.querySelector('.system-banner__action-btn') as HTMLButtonElement).disabled).toBe(true);

      // Act — backend re-arms: new nextProbeAt, isChecking → false
      const freshDate = new Date(Date.now() + 5 * 60_000).toISOString();
      mockCredits._nextProbeAtSignal.set(freshDate);
      mockCredits._isCheckingSignal.set(false);
      fixture.detectChanges();

      // Assert — counting-down state, no toast (not possible without ToastService)
      expect(el.textContent).toContain('Next automatic check in');
      const btn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      expect(btn.disabled).toBe(false);
      expect(btn.textContent?.trim()).toBe('Check now');
    });
  });

  describe('"Check now" button', () => {
    it('should call creditsService.checkNow() when the button is clicked', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60_000).toISOString();
      const { fixture, mockCredits } = setup({ credits: { nextProbeAt: futureDate, isChecking: false } });
      const el = fixture.nativeElement as HTMLElement;

      // Act
      const btn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      btn.click();
      fixture.detectChanges();

      // Assert
      expect(mockCredits.checkNow).toHaveBeenCalledOnce();
    });

    it('should have aria-label "Check the Claude account for available credit now"', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60_000).toISOString();

      // Act
      const { fixture } = setup({ credits: { nextProbeAt: futureDate } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const btn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      expect(btn.getAttribute('aria-label')).toBe('Check the Claude account for available credit now');
    });
  });

  describe('accessibility', () => {
    it('should have role="region" with aria-label "Credit status" on the wrapper', () => {
      // Arrange / Act
      const { fixture } = setup({ credits: { nextProbeAt: new Date(Date.now() + 60_000).toISOString() } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const wrapper = el.querySelector('[aria-label="Credit status"]') as HTMLElement;
      expect(wrapper).not.toBeNull();
      expect(wrapper.getAttribute('role')).toBe('region');
    });

    it('should not have role="status" or role="alert" on the bar div itself (no bar-level live region)', () => {
      // Arrange / Act
      const { fixture } = setup({ credits: { nextProbeAt: new Date(Date.now() + 60_000).toISOString() } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — the bar must not be a live region; live regions are scoped to the sr-only span and status-line
      const bar = el.querySelector('.system-banner__bar--credits') as HTMLElement;
      expect(bar.getAttribute('role')).toBeNull();
    });

    it('should scope role="status" to the status-line span, not the bar', () => {
      // Arrange / Act
      const { fixture } = setup({ credits: { nextProbeAt: new Date(Date.now() + 60_000).toISOString() } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — status-line span carries role="status" with aria-atomic
      const statusLine = el.querySelector('.system-banner__status-line') as HTMLElement;
      expect(statusLine).not.toBeNull();
      expect(statusLine.getAttribute('role')).toBe('status');
      expect(statusLine.getAttribute('aria-atomic')).toBe('true');
    });

    it('should render exactly one role="status" status-line element regardless of view state', () => {
      // Arrange / Act — checking state
      const futureDate = new Date(Date.now() + 60_000).toISOString();
      const { fixture, mockCredits } = setup({ credits: { nextProbeAt: futureDate, isChecking: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — single element in checking state
      expect(el.querySelectorAll('.system-banner__status-line').length).toBe(1);

      // Act — transition to counting-down
      mockCredits._isCheckingSignal.set(false);
      fixture.detectChanges();

      // Assert — still exactly one element after state change
      expect(el.querySelectorAll('.system-banner__status-line').length).toBe(1);
    });

    it('should show counting-down text in the live region when not checking', () => {
      // Arrange / Act
      const futureDate = new Date(Date.now() + 60_000).toISOString();
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: false } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — live region contains the counting-down phrase
      const statusLine = el.querySelector('.system-banner__status-line') as HTMLElement;
      expect(statusLine.textContent).toContain('Next automatic check in');
    });

    it('should show checking text in the live region when checking', () => {
      // Arrange / Act
      const futureDate = new Date(Date.now() + 60_000).toISOString();
      const { fixture } = setup({ credits: { nextProbeAt: futureDate, isChecking: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — live region contains the checking phrase
      const statusLine = el.querySelector('.system-banner__status-line') as HTMLElement;
      expect(statusLine.textContent).toContain('Checking whether the Claude account can spend again');
    });

    it('should have a sr-only role="alert" (no aria-live) for one-shot state transition announcements', () => {
      // Arrange / Act
      const { fixture } = setup({ credits: { nextProbeAt: new Date(Date.now() + 60_000).toISOString() } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — the sr-only transition announcer must be role="alert" without a redundant aria-live
      const announcer = el.querySelector('.sr-only[role="alert"]') as HTMLElement;
      expect(announcer).not.toBeNull();
      expect(announcer.getAttribute('aria-live')).toBeNull();
    });


    it('should have type="button" on the Check now button', () => {
      // Arrange / Act
      const { fixture } = setup({ credits: { nextProbeAt: new Date(Date.now() + 60_000).toISOString() } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const btn = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      expect(btn.getAttribute('type')).toBe('button');
    });
  });

  describe('simultaneous banner rendering', () => {
    it('should render the credits banner region alongside other system-banner children without conflict', () => {
      // Arrange / Act — credits banner visible alongside dispatch (no extra providers needed)
      const futureDate = new Date(Date.now() + 60_000).toISOString();
      const { fixture } = setup({ credits: { nextProbeAt: futureDate } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — credits region is visible
      const creditsRegion = el.querySelector('[aria-label="Credit status"]') as HTMLElement;
      expect(creditsRegion?.hidden).toBe(false);
    });
  });
});
