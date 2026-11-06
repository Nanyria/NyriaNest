import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { ILoggedInUser } from '../../../Models/interfaces';
import { AuthService } from '../../../Services/auth.service';
import { UserActionsService } from '../../../Services/user-actions.service';

@Component({
  selector: 'app-user-settings',
  standalone: true,
  templateUrl: './user-settings-component.html',
  styleUrls: ['./user-settings-component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [FormsModule],
})
export class UserSettingsComponent {
  editMode = false;
  changePasswordMode = false;
  oldPassword = '';
  newPassword = '';
  confirmPassword = '';
  user: ILoggedInUser | null = null;

  constructor(
    private authService: AuthService,
    private userActionsService: UserActionsService,
  ) {}

  ngOnInit(): void {
    this.authService.getCurrentUser().subscribe((user) => {
      this.user = user;
    });
  }

  submitPasswordChange() {
    if (!this.user) return;
    if (!this.oldPassword || !this.newPassword || !this.confirmPassword) {
      alert('Alla fält måste fyllas i.');
      return;
    }
    if (this.newPassword !== this.confirmPassword) {
      alert('Nya lösenorden matchar inte.');
      return;
    }

    // Prepare the update payload
    // The API checks currentPassword before it changes the password
    const updatePayload = {
      password: this.newPassword,
      currentPassword: this.oldPassword,
    };

    this.userActionsService
      .updateUser(updatePayload)
      .subscribe({
        next: () => {
          alert('Lösenordet har ändrats!');
          this.changePasswordMode = false;
          this.oldPassword = '';
          this.newPassword = '';
          this.confirmPassword = '';
        },
        // Show the API's reason, e.g. a wrong current password or a too weak new one
        error: (err) => {
          const messages: string[] = err?.error?.errorMessages ?? [];
          alert(
            messages.length
              ? messages.join('\n')
              : "Your password couldn't be changed. Please try again.",
          );
        },
      });
  }

  submitNameChange() {
    if (!this.user) return;
    if (!this.user.firstName || !this.user.lastName) {
      alert('Förnamn och efternamn måste fyllas i.');
      return;
    }

    const updatePayload = {
      firstName: this.user.firstName,
      lastName: this.user.lastName,
      profilePictureUrl: this.user.profilePictureUrl,
      bio: this.user.bio,
    };

    this.userActionsService
      .updateUser(updatePayload)
      .subscribe({
        next: () => {
          alert('Info har uppdaterats!');
          this.editMode = false;
        },
        error: () => {
          alert('Kunde inte uppdatera info.');
        },
      });
  }
  profilePictures: string[] = [
    'assets/images/profile-pictures/BlueKidDragon.svg',
    'assets/images/profile-pictures/PinkKidDragon.svg',
    'assets/images/profile-pictures/RedDragon.svg',
  ];

  selectProfilePicture(pic: string) {
    if (this.user) {
      this.user.profilePictureUrl = pic;
    }
  }
}
