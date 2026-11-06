import {
  Component,
  OnInit,
  ViewChild,
  ChangeDetectionStrategy,
} from '@angular/core';

import { FormsModule } from '@angular/forms';
import { UserActionsService } from '../../../Services/user-actions.service';
import { AuthService } from '../../../Services/auth.service';
import { ILoggedInUser } from '../../../Models/interfaces';
import { UserSettingsComponent } from '../user-settings/user-settings-component';
import { UserReservationsComponent } from '../user-reservations/user-reservations-component';
import { UserReadlistComponent } from '../user-readlist/user-readlist-component';
import { UserCheckoutsComponent } from '../user-checkouts/user-checkouts-component';
import {
  Router,
  RouterModule,
  NavigationEnd,
} from '@angular/router';
import { filter } from 'rxjs/operators';
import { UserPageNavComponent } from '../user-page-nav/user-page-nav.component';
@Component({
  selector: 'app-user-page',
  standalone: true,
  templateUrl: './user-page.component.html',
  styleUrls: ['./user-page.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [FormsModule, RouterModule, UserPageNavComponent],
})
export class UserPageComponent implements OnInit {
  user: ILoggedInUser | null = null;
  checkedOutBooks: any[] = [];
  reservedBooks: any[] = [];
  readList: any[] = [];
  editMode = false;
  hasNewNotification = false;
  currentUrl: string = '';

  @ViewChild(UserReservationsComponent)
  reservationsComp?: UserReservationsComponent;
  @ViewChild(UserCheckoutsComponent) checkoutsComp?: UserCheckoutsComponent;
  @ViewChild(UserReadlistComponent) readlistComp?: UserReadlistComponent;
  @ViewChild(UserSettingsComponent) settingsComp?: UserSettingsComponent;

  constructor(
    private userService: UserActionsService,
    private authService: AuthService,
    private router: Router,
  ) {
    // Subscribe to router events to update currentUrl
    this.router.events
      .pipe(
        filter(
          (event): event is NavigationEnd => event instanceof NavigationEnd,
        ),
      )
      .subscribe((event) => {
        this.currentUrl = event.urlAfterRedirects;
      });
  }

  ngOnInit(): void {
    this.authService.getCurrentUser().subscribe((user) => {
      this.user = user;
      console.log('User:', this.user);
      this.reservedBooks = user?.reservedBooks || [];
      console.log('Reserved Books:', this.reservedBooks);
      this.checkedOutBooks = user?.checkedOutBooks || [];
      console.log('Checked Out Books:', this.checkedOutBooks);
    });
    this.currentUrl = this.router.url;
  }

  updateUserInfo() {
    if (this.user) {
      this.userService.updateUser(this.user).subscribe((updated) => {
        this.user = updated;
        this.editMode = false;
        this.reservedBooks = updated.reservedBooks || [];
        this.checkedOutBooks = updated.checkedOutBooks || [];
      });
    }
  }

  refreshReadlist() {
    // this.readlistComp?.refresh();
  }
}
