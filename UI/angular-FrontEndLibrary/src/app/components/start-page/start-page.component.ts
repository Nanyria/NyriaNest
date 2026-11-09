import { Component, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, RouterModule } from '@angular/router';

import { LoginComponent } from '../Users/login-page/login.component';
import { RegisterUserComponent } from '../Users/register-user/register-user.component';
import { ForgotPasswordComponent } from '../Users/password-reset/forgot-password.component';
import { ResetPasswordComponent } from '../Users/password-reset/reset-password.component';

// What the card shows: log in (or register), or the two steps of resetting a
// forgotten password. The routes set it (app.routes.ts, data.mode).
export type StartPageMode = 'login' | 'forgot' | 'reset';

@Component({
  selector: 'app-start-page',
  standalone: true,
  imports: [LoginComponent, RegisterUserComponent, ForgotPasswordComponent, ResetPasswordComponent, RouterModule],
  templateUrl: './start-page.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./start-page.component.css'],
})
export class StartPageComponent {
  showRegister = false;
  readonly mode: StartPageMode;

  constructor(route: ActivatedRoute) {
    this.mode = (route.snapshot.data['mode'] as StartPageMode) ?? 'login';
  }
}
