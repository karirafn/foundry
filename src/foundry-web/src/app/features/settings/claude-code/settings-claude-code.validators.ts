import { AbstractControl, ValidationErrors } from '@angular/forms';

// Identical to the server's deny-rule pattern in ClaudeCodeSettings.Create.
export const DENY_RULE_PATTERN = /^(Bash|Edit|Read|Write|MultiEdit|Glob|Grep)\(.+\)$/;

export function denyRuleFormatValidator(control: AbstractControl): ValidationErrors | null {
  const value: string = control.value;
  if (!value) {
    return null;
  }
  return DENY_RULE_PATTERN.test(value) ? null : { denyRuleFormat: true };
}
