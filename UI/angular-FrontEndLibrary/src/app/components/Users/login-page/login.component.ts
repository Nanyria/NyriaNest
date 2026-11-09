import { Component, ChangeDetectionStrategy } from '@angular/core';
import { AuthService } from '../../../Services/auth.service';
import { Router, RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';

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

  constructor(
    private authService: AuthService,
    private router: Router,
  ) {}

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
