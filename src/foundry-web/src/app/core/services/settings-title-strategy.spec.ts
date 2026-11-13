import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot } from '@angular/router';
import { SettingsTitleStrategy } from './settings-title-strategy';

function buildSnapshot(url = '/settings/dispatch'): RouterStateSnapshot {
  return {
    url,
    root: {},
  } as unknown as RouterStateSnapshot;
}

describe('SettingsTitleStrategy', () => {
  let strategy: SettingsTitleStrategy;
  let titleService: Title;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SettingsTitleStrategy, Title],
    });
    strategy = TestBed.inject(SettingsTitleStrategy);
    titleService = TestBed.inject(Title);
  });

  // Cycle 1: composes "Dispatch — Foundry Settings" when buildTitle returns "Dispatch"
  it('should set document title to "Dispatch — Foundry Settings" when route title is "Dispatch"', () => {
    // Arrange
    const snapshot = buildSnapshot();
    vi.spyOn(strategy, 'buildTitle').mockReturnValue('Dispatch');
    vi.spyOn(titleService, 'setTitle');

    // Act
    strategy.updateTitle(snapshot);

    // Assert
    expect(titleService.setTitle).toHaveBeenCalledWith('Dispatch — Foundry Settings');
  });

  // Cycle 2: leaves document title unchanged when no route title
  it('should not update document title when buildTitle returns undefined', () => {
    // Arrange
    const snapshot = buildSnapshot();
    vi.spyOn(strategy, 'buildTitle').mockReturnValue(undefined);
    vi.spyOn(titleService, 'setTitle');

    // Act
    strategy.updateTitle(snapshot);

    // Assert
    expect(titleService.setTitle).not.toHaveBeenCalled();
  });

  // Cycle 3: suffix is exactly " — Foundry Settings" (em dash U+2014)
  it('should append exactly " — Foundry Settings" suffix to the page title', () => {
    // Arrange
    const snapshot = buildSnapshot();
    vi.spyOn(strategy, 'buildTitle').mockReturnValue('Prompts');
    vi.spyOn(titleService, 'setTitle');

    // Act
    strategy.updateTitle(snapshot);

    // Assert
    const called = (titleService.setTitle as ReturnType<typeof vi.spyOn>).mock.calls[0][0] as string;
    expect(called.endsWith(' — Foundry Settings')).toBe(true);
  });

  it('uses the plain Foundry suffix outside settings', () => {
    // Arrange
    const snapshot = buildSnapshot('/');
    vi.spyOn(strategy, 'buildTitle').mockReturnValue('Board');
    vi.spyOn(titleService, 'setTitle');

    // Act
    strategy.updateTitle(snapshot);

    // Assert
    expect(titleService.setTitle).toHaveBeenCalledWith('Board — Foundry');
  });

  it('does not read a path that merely starts with "settings" as a settings page', () => {
    // Arrange
    const snapshot = buildSnapshot('/settingsx');
    vi.spyOn(strategy, 'buildTitle').mockReturnValue('Other');
    vi.spyOn(titleService, 'setTitle');

    // Act
    strategy.updateTitle(snapshot);

    // Assert
    expect(titleService.setTitle).toHaveBeenCalledWith('Other — Foundry');
  });

  it('keeps the settings suffix for the settings root', () => {
    // Arrange
    const snapshot = buildSnapshot('/settings');
    vi.spyOn(strategy, 'buildTitle').mockReturnValue('Settings');
    vi.spyOn(titleService, 'setTitle');

    // Act
    strategy.updateTitle(snapshot);

    // Assert
    expect(titleService.setTitle).toHaveBeenCalledWith('Settings — Foundry Settings');
  });
});
