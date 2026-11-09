import { Component, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../Services/auth.service';

// "I forgot my password": asks for the email and sends a reset link. The answer is
// the same whether or not an account has that email, so it shows the API's message.
@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [FormsModule, RouterModule],
  templateUrl: './forgot-password.component.html',
  styleUrls: ['./password-reset.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ForgotPasswordComponent implements OnInit {
  email = '';
  submitted = false;
  sending = false;
  sentMessage = '';
  errorMessage = '';

  constructor(
    private authService: AuthService,
    private route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Settings links here with the user's email filled in
    this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
  }

  send(form: NgForm): void {
    this.submitted = true;
    this.errorMessage = '';
    if (form.invalid) return;

    this.sending = true;
    this.authService.forgotPassword(this.email.trim()).subscribe({
      next: (response) => {
        this.sending = false;
        this.sentMessage = response.result;
      },
      error: (err: HttpErrorResponse) => {
        this.sending = false;
        this.errorMessage = err.status === 429
          ? 'Too many tries. Please wait a few minutes and try again.'
          : "Something went wrong and no link was sent. Please try again.";
      },
    });
  }
}
