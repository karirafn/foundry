import { AbstractControl, ValidationErrors } from '@angular/forms';

// Identical to the server's deny-rule pattern in ClaudeCodeSettings.Create.
export const DENY_RULE_PATTERN = /^(Bash|Edit|Read|Write|MultiEdit|Glob|Grep)\(.+\)$/;

// Mirrors the CiCdDenyList constant in WorkerSettingsBuilder. Display-only: never sent to the server.
export const CI_CD_DENY_RULES: readonly string[] = [
  'Edit(**/.github/workflows/**)',
  'Edit(**/.gitlab-ci.yml)',
  'Edit(**/Dockerfile)',
  'Edit(**/docker-compose*.yml)',
  'Edit(**/docker-compose.yaml)',
  'Edit(**/compose.yml)',
  'Edit(**/compose.yaml)',
];

export function denyRuleFormatValidator(control: AbstractControl): ValidationErrors | null {
  const value: string = control.value;
  if (!value) {
    return null;
  }
  return DENY_RULE_PATTERN.test(value) ? null : { denyRuleFormat: true };
}
