import { components } from '../../api/schema';

/** One tool a worker image or run carries, as the image's toolchain script reported it (ADR 0109). */
export interface ToolchainEntry {
  readonly name: string;
  readonly version: string;
}

/** The toolchain of the image workers run now: `recorded` once Foundry probed it, `unknown` until then. */
export type WorkerImageToolchain = components['schemas']['WorkerImageToolchainResponse'];
