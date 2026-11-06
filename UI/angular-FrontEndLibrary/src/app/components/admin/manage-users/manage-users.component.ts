import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { UserDto } from '../../../Models/interfaces';
import { UserService } from '../../../Services/user.service';
import { ConfirmService } from '../../../Services/confirm.service';
@Component({
  selector: 'app-manage-users',
  standalone: true,
  templateUrl: './manage-users.component.html',
  styleUrls: ['./manage-users.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [FormsModule],
})
export class ManageUsersComponent implements OnInit {
  editMode = false;

  users: UserDto[] = [];
  editUser: UserDto | null = null;
  roleOptions = [
    { value: 'User', label: 'User' },
    { value: 'Admin', label: 'Admin' },
    { value: 'SuperAdmin', label: 'Super Admin' },
  ];

  isSuperAdmin: boolean = false;

  constructor(
    private userService: UserService,
    private confirmDialog: ConfirmService,
  ) {}

  ngOnInit() {
    this.getAllUsers();
  }

  getAllUsers() {
    this.userService.getAllUsers().subscribe(
      (response: { isSuccess: boolean; result: UserDto[] }) => {
        console.log('Users fetched:', response);
        if (response.isSuccess && Array.isArray(response.result)) {
          this.users = response.result;
          console.log('Users array:', this.users);
        } else {
          this.users = [];
        }
      },
      (error) => {
        console.error('Error fetching users', error);
        this.users = [];
      },
    );
  }
  getRoleLabel(roleValue: string): string {
    const found = this.roleOptions.find((r) => r.value === roleValue);
    return found ? found.label : roleValue;
  }
  populateForm(user: UserDto) {
    this.editUser = { ...user };
  }
  // Deleting can't be undone and removes the user's loans, reservations and reviews, so it asks first
  async deleteUser(user: UserDto) {
    const confirmed = await this.confirmDialog.confirm({
      title: `Delete ${user.firstName} ${user.lastName}?`,
      message: `The account "${user.userName}" and its loans, reservations and reviews will be removed. This can't be undone.`,
      confirmLabel: 'Delete user',
      cancelLabel: 'Keep user',
      danger: true,
      icon: 'trash',
    });
    if (!confirmed) return;

    this.userService.deleteUser(user.userId).subscribe({
      next: () => this.getAllUsers(),
      error: (err) => this.showError(err, "The user couldn't be deleted."),
    });
  }

  // The API says why, e.g. that an Admin can't delete or change another admin
  private showError(err: any, fallback: string) {
    const messages: string[] = err?.error?.errorMessages ?? [];
    alert(messages.length ? messages.join('\n') : fallback);
  }
  resetForm() {
    this.editUser = null;
  }
  cancelEdit() {
    this.resetForm();
  }
  saveUser(user: UserDto) {
    if (this.editUser) {
      this.userService
        .updateUserAsAdmin(this.editUser.userId, user)
        .subscribe({
          next: () => {
            this.getAllUsers();
            this.resetForm();
          },
          error: (err) => this.showError(err, "The changes couldn't be saved."),
        });
    } else {
      this.userService.createAdminUser(user).subscribe(() => {
        this.getAllUsers();
        this.resetForm();
      });
    }
  }
}
