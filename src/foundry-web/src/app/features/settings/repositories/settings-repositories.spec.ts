import { TestBed } from '@angular/core/testing';
import { SettingsRepositoriesComponent } from './settings-repositories';

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsRepositoriesComponent],
  });

  const fixture = TestBed.createComponent(SettingsRepositoriesComponent);

  return { fixture };
}

describe('SettingsRepositoriesComponent', () => {
  it('should render a section card with "Repositories" heading', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const section = el.querySelector('.repositories-placeholder__section');
    expect(section).toBeTruthy();
    const heading = el.querySelector('.repositories-placeholder__heading');
    expect(heading?.textContent?.trim()).toBe('Repositories');
  });

  it('should render "Repository management is coming soon." subtext', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const subtext = el.querySelector('.repositories-placeholder__subtext');
    expect(subtext?.textContent?.trim()).toBe('Repository management is coming soon.');
  });
});
