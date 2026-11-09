import { Component, ChangeDetectionStrategy } from '@angular/core';
import { AuthService } from '../../../Services/auth.service';
import { Router, RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { SESSION_ENDED_KEY, sessionEnd } from '../../../Services/auth.inceptor';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css'],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [FormsModule, RouterModule],
})
export class LoginComponent {
  username: string = '';
  password: string = '';
  errorMessage: string = '';
  showPassword = false;
  // Why the app just logged the user out, if it did (set by AuthInterceptor). Shown once.
  sessionNotice = '';

  constructor(
    private authService: AuthService,
    private router: Router,
  ) {
    if (sessionEnd.reloading) return; // the reloaded page shows the notice
    const reason = sessionStorage.getItem(SESSION_ENDED_KEY);
    sessionStorage.removeItem(SESSION_ENDED_KEY);
    if (reason === 'credentials-changed') {
      this.sessionNotice = "You've been logged out because your password or email was changed. Log in again to continue.";
    } else if (reason) {
      this.sessionNotice = 'Your session has ended. Log in again to continue.';
    }
  }

  onLogin(): void {
    this.authService.login(this.username, this.password).subscribe({
      next: () => {
        // Wait for the token to be set before navigating
        setTimeout(() => {
          this.router.navigate(['/home']);
        }, 0);
      },
      error: () => {
        this.errorMessage = 'Invalid username or password';
      },
    });
  }
}
