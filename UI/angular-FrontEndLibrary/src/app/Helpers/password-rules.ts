import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// The API's password rules (Identity options in Program.cs). Register and
// Settings list them before the user types, and tick them off as they're met.
export interface PasswordRule {
  label: string;
  test: (password: string) => boolean;
}

export const PASSWORD_RULES: PasswordRule[] = [
  { label: 'At least 8 characters', test: (pw) => pw.length >= 8 },
  { label: 'An uppercase letter', test: (pw) => /[A-Z]/.test(pw) },
  { label: 'A lowercase letter', test: (pw) => /[a-z]/.test(pw) },
  { label: 'A digit', test: (pw) => /\d/.test(pw) },
  // Identity counts anything that isn't a letter or a digit
  { label: 'A symbol, like ! or #', test: (pw) => /[^a-zA-Z0-9]/.test(pw) },
];

export const meetsPasswordRules = (password: string): boolean =>
  PASSWORD_RULES.every((rule) => rule.test(password));

export const passwordRulesValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null =>
  !control.value || meetsPasswordRules(control.value) ? null : { passwordRules: true };
