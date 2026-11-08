import { Component, ChangeDetectionStrategy } from '@angular/core';

import { FormsModule, NgForm } from '@angular/forms';
import { UserActionsService } from '../../../Services/user-actions.service';
import { CreateUserDto } from '../../../Models/interfaces';
import { meetsPasswordRules } from '../../../Helpers/password-rules';
@Component({
  selector: 'app-register-user',
  templateUrl: './register-user.component.html',
  standalone: true,
  styleUrls: ['./register-user.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [FormsModule],
})
export class RegisterUserComponent {
  user: CreateUserDto = {
    userName: '',
    firstName: '',
    lastName: '',
    email: '',
    password: '',
  };
  registrationSuccess = false;
  confirmPassword: string = '';
  showPassword = false;
  errorMessage = '';
  submitted = false;
  constructor(private userActionsService: UserActionsService) {}

  // The submit button stays enabled; problems are shown after the first attempt
  onSubmit(form: NgForm) {
    this.submitted = true;
    if (form.invalid || !this.passwordValid() || !this.passwordsMatch()) {
      return;
    }
    this.registerUser();
  }

  passwordsMatch(): boolean {
    return this.user.password === this.confirmPassword;
  }

  registerUser() {
    this.errorMessage = '';
    this.userActionsService.addUser(this.user).subscribe({
      next: (response) => {
        console.log('User registered successfully:', response);
        this.registrationSuccess = true;
      },
      error: (error) => {
        console.error('Error registering user:', error);
        this.errorMessage = 'Something went wrong. Please try again.';
      },
    });
  }
  // Same rules as the API and Settings
  passwordValid(): boolean {
    return meetsPasswordRules(this.user.password);
  }
}
