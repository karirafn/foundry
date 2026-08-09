import { TestBed } from '@angular/core/testing';
import { AccountPresenceService } from './account-presence.service';

function setup() {
  TestBed.configureTestingModule({
    providers: [AccountPresenceService],
  });
  return TestBed.inject(AccountPresenceService);
}

describe('AccountPresenceService', () => {
  // Cycle 1: default hasAccounts is false
  it('should default hasAccounts to false', () => {
    // Arrange
    const service = setup();

    // Act / Assert
    expect(service.hasAccounts()).toBe(false);
  });

  // Cycle 2: setHasAccounts(true) flips to true
  it('should reflect true after setHasAccounts(true) is called', () => {
    // Arrange
    const service = setup();

    // Act
    service.setHasAccounts(true);

    // Assert
    expect(service.hasAccounts()).toBe(true);
  });

  // Cycle 3: setHasAccounts(false) flips back to false
  it('should reflect false after setHasAccounts(false) is called following true', () => {
    // Arrange
    const service = setup();
    service.setHasAccounts(true);

    // Act
    service.setHasAccounts(false);

    // Assert
    expect(service.hasAccounts()).toBe(false);
  });
});
