import { Component, ChangeDetectionStrategy } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../Services/auth.service';
import { ILoggedInUser } from '../../Models/interfaces';
import { ADMIN_NAV, MORE_LINKS, SETTINGS_NAV } from '../layout/nav-items';

// The bottom bar's More tab on small screens: everything without its own tab
@Component({
  selector: 'app-more-page',
  standalone: true,
  templateUrl: './more-page.component.html',
  styleUrls: ['./more-page.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [RouterModule, NgTemplateOutlet],
})
export class MorePageComponent {
  readonly links = MORE_LINKS;
  readonly adminGroup = ADMIN_NAV;
  readonly settings = SETTINGS_NAV;
  currentUser: ILoggedInUser | null = null;
  adminOpen = false;

  constructor(
    private authService: AuthService,
    private router: Router,
  ) {
    this.authService.getCurrentUser().subscribe((user) => {
      this.currentUser = user;
    });
  }

  get isAdmin(): boolean {
    return !!this.currentUser?.adminRole || !!this.currentUser?.isSuperAdmin;
  }

  logout(): void {
    this.authService.logout();
    this.router.navigate(['']);
  }
}
