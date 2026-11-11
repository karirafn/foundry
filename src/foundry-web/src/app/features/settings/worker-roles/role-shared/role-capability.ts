import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { WorkerCapability } from '../worker-role.service';

interface CapabilityProfile {
  readonly label: string;
  readonly modifier: WorkerCapability;
  readonly hint: string;
}

interface CapabilityDisplay {
  readonly label: string;
  readonly classes: string;
  /** Restricted roles carry a lock icon. */
  readonly locked: boolean;
}

/** Keyed by every `WorkerCapability`, so the compiler demands an entry for any new value. */
const CAPABILITY_PROFILES: Record<WorkerCapability, CapabilityProfile> = {
  restricted: {
    label: 'Restricted',
    modifier: 'restricted',
    hint: 'Runs read-only: no command tools, no provider token and no branch. It returns a verdict only.',
  },
  full: {
    label: 'Full access',
    modifier: 'full',
    hint: 'Runs with command tools and the account token, on its own branch.',
  },
};

const CAPABILITY_BADGE_CLASS = 'role-capability';

const UNKNOWN_CAPABILITY: CapabilityDisplay = {
  label: 'Unknown capability',
  classes: CAPABILITY_BADGE_CLASS,
  locked: false,
};

function isWorkerCapability(value: string): value is WorkerCapability {
  return Object.hasOwn(CAPABILITY_PROFILES, value);
}

/** The sentence explaining what a capability lets a role do; a value this build doesn't know has none. */
export function capabilityHint(capability: string): string | null {
  return isWorkerCapability(capability) ? CAPABILITY_PROFILES[capability].hint : null;
}

/** A value this build doesn't know gets a neutral badge. */
function toCapabilityDisplay(capability: string): CapabilityDisplay {
  if (!isWorkerCapability(capability)) {
    return UNKNOWN_CAPABILITY;
  }
  const profile = CAPABILITY_PROFILES[capability];
  return {
    label: profile.label,
    classes: `${CAPABILITY_BADGE_CLASS} ${CAPABILITY_BADGE_CLASS}--${profile.modifier}`,
    locked: profile.modifier === 'restricted',
  };
}

/** A role's capability profile as a small badge; screen readers hear "Capability: <label>". */
@Component({
  selector: 'fd-role-capability',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span [class]="display().classes">
      <span class="sr-only">Capability: </span>
      @if (display().locked) {
        <svg
          class="role-capability__icon"
          xmlns="http://www.w3.org/2000/svg"
          width="11"
          height="11"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2.5"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
          focusable="false"
        >
          <rect x="4" y="11" width="16" height="10" rx="2" ry="2" />
          <path d="M8 11V7a4 4 0 0 1 8 0v4" />
        </svg>
      }
      {{ display().label }}
    </span>
  `,
  styleUrl: './role-capability.scss',
})
export class RoleCapabilityComponent {
  readonly capability: InputSignal<string> = input.required<string>();

  protected readonly display: Signal<CapabilityDisplay> = computed(() =>
    toCapabilityDisplay(this.capability()),
  );
}
