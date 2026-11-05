import {
  Component,
  Input,
  Output,
  EventEmitter,
  OnInit,
  ChangeDetectionStrategy,
} from '@angular/core';

import { FormsModule } from '@angular/forms';
import { ILoggedInUser, UserDto } from '../../../Models/interfaces';
import { UserService } from '../../../Services/user.service';
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

  constructor(private userService: UserService) {}

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
  handleFormSubmit(user: UserDto) {
    if (this.editUser) {
      this.userService
        .updateUserAsAdmin(this.editUser.id, user)
        .subscribe(() => {
          this.getAllUsers();
          this.resetForm();
        });
    } else {
      this.userService.createAdminUser(user).subscribe(() => {
        this.getAllUsers();
        this.resetForm();
      });
    }
  }

  populateForm(user: UserDto) {
    this.editUser = { ...user };
  }
  deleteUser(id: string) {
    this.userService.deleteUser(id).subscribe(() => {
      this.getAllUsers();
    });
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
        .updateUserAsAdmin(this.editUser.id, user)
        .subscribe(() => {
          this.getAllUsers();
          this.resetForm();
        });
    } else {
      this.userService.createAdminUser(user).subscribe(() => {
        this.getAllUsers();
        this.resetForm();
      });
    }
  }
}
