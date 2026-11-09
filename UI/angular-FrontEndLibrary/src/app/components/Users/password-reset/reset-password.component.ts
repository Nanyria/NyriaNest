import { Component, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../Services/auth.service';
import { PASSWORD_RULES, meetsPasswordRules } from '../../../Helpers/password-rules';

// Opened from the emailed link (/reset-password?email=...&token=...): choose a new password
@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [FormsModule, RouterModule],
  templateUrl: './reset-password.component.html',
  styleUrls: ['./password-reset.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ResetPasswordComponent implements OnInit {
  // The API's messages that mean the link itself is no good
  private static readonly INVALID_LINK = 'This reset link is invalid or has expired.';

  readonly passwordRules = PASSWORD_RULES;

  email = '';
  token = '';
  newPassword = '';
  confirmPassword = '';
  showPassword = false;
  newPasswordFocused = false;

  submitted = false;
  saving = false;
  done = false;
  linkInvalid = false;
  errors: string[] = [];

  constructor(
    private authService: AuthService,
    private route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    const query = this.route.snapshot.queryParamMap;
    this.email = query.get('email') ?? '';
    this.token = query.get('token') ?? '';
    // A link cut short in the email client: nothing to try with
    this.linkInvalid = !this.email || !this.token;
  }

  get passwordValid(): boolean {
    return meetsPasswordRules(this.newPassword);
  }

  get passwordsMatch(): boolean {
    return this.newPassword === this.confirmPassword;
  }

  get missingRules(): string {
    return this.passwordRules
      .filter((rule) => !rule.test(this.newPassword))
      .map((rule) => rule.label.toLowerCase())
      .join(', ');
  }

  save(form: NgForm): void {
    this.submitted = true;
    this.errors = [];
    if (form.invalid || !this.passwordValid || !this.passwordsMatch) return;

    this.saving = true;
    this.authService.resetPassword(this.email, this.token, this.newPassword).subscribe({
      next: () => {
        this.saving = false;
        this.done = true;
      },
      error: (err: HttpErrorResponse) => {
        this.saving = false;
        if (err.status === 429) {
          this.errors = ['Too many tries. Please wait a few minutes and try again.'];
          return;
        }
        const messages: string[] = err?.error?.errorMessages ?? [];
        if (messages.includes(ResetPasswordComponent.INVALID_LINK)) {
          this.linkInvalid = true;
        } else {
          this.errors = messages.length ? messages : ["Your password couldn't be changed. Please try again."];
        }
      },
    });
  }
}
