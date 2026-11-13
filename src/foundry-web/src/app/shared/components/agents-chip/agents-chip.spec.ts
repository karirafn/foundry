import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import {
  FakeConnectionGate,
  provideFakeConnectionGate,
} from '../../../core/testing/fake-connection-gate';
import { AgentsChipComponent } from './agents-chip';

function setup(gate = new FakeConnectionGate()) {
  TestBed.configureTestingModule({
    imports: [AgentsChipComponent],
    providers: [
      provideRouter([]),
      provideFakeConnectionGate(gate),
      { provide: WorkerSignalRService, useValue: { activeAgentCount: signal(3) } },
    ],
  });
  const fixture = TestBed.createComponent(AgentsChipComponent);
  fixture.detectChanges();
  return { fixture, gate, el: fixture.nativeElement as HTMLElement };
}

describe('AgentsChipComponent', () => {
  it('links to the Floor with the working count while live', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const link = el.querySelector('a.agents-chip');
    expect(link?.getAttribute('aria-label')).toBe('3 agents working. Open the Floor');
    expect(link?.getAttribute('href')).toBe('/floor');
  });

  it('shows a placeholder image, out of the tab order, while the gate is up', () => {
    // Arrange
    const { fixture, gate, el } = setup();

    // Act
    gate.phase.set('reconnecting');
    fixture.detectChanges();

    // Assert
    const placeholder = el.querySelector('.agents-chip--placeholder');
    expect(placeholder?.getAttribute('role')).toBe('img');
    expect(placeholder?.getAttribute('aria-label')).toBe(
      'Agent count unavailable while reconnecting',
    );
    expect(el.querySelector('a, button, [tabindex]')).toBeNull();
    expect(el.textContent).not.toContain('3');
    expect(el.querySelector('.agents-chip__text')?.textContent).toBe('— agents working');
  });

  it('keeps the count during the grace period', () => {
    // Arrange
    const { fixture, gate, el } = setup();

    // Act
    gate.phase.set('grace');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.agents-chip--placeholder')).toBeNull();
    expect(el.querySelector('.agents-chip__text')?.textContent).toBe('3 agents working');
  });
});
