import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SettingsWorkerEnvironmentComponent } from './settings-worker-environment';

function setup(): ComponentFixture<SettingsWorkerEnvironmentComponent> {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsWorkerEnvironmentComponent],
  });
  const fixture = TestBed.createComponent(SettingsWorkerEnvironmentComponent);
  fixture.detectChanges();
  return fixture;
}

describe('SettingsWorkerEnvironmentComponent', () => {
  // Cycle 1: empty-state copy renders the section heading
  it('should render the Worker environment section heading', () => {
    // Arrange
    const fixture = setup();

    // Act
    const heading = fixture.nativeElement.querySelector('h2');

    // Assert
    expect(heading).not.toBeNull();
    expect(heading.textContent).toContain('Worker environment');
  });

  // Cycle 2: names container limits in the list
  it('should mention container limits in the empty-state list', () => {
    // Arrange
    const fixture = setup();

    // Act
    const text: string = fixture.nativeElement.textContent;

    // Assert
    expect(text).toContain('Container limits');
  });

  // Cycle 3: names mounts in the list
  it('should mention mounts in the empty-state list', () => {
    // Arrange
    const fixture = setup();

    // Act
    const text: string = fixture.nativeElement.textContent;

    // Assert
    expect(text).toContain('Mounts');
  });

  // Cycle 4: names host hooks in the list
  it('should mention host hooks in the empty-state list', () => {
    // Arrange
    const fixture = setup();

    // Act
    const text: string = fixture.nativeElement.textContent;

    // Assert
    expect(text).toContain('Host hooks');
  });

  // Cycle 5: hasUnsavedChanges returns false (no editable form)
  it('should report no unsaved changes', () => {
    // Arrange
    const fixture = setup();

    // Act
    const result = fixture.componentInstance.hasUnsavedChanges();

    // Assert
    expect(result).toBe(false);
  });
});
