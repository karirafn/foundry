import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { setupSettingsPage, flushSettings } from '../testing/settings-page-harness';
import { SettingsGeneralComponent } from './settings-general';

describe('SettingsGeneralComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should render without errors (shell placeholder before step 9 removes this file)', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Assert — component renders without throwing
    expect(fixture.nativeElement).toBeTruthy();
  });
});
