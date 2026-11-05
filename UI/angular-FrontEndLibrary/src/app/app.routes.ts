import { Routes } from '@angular/router';
import { LibraryComponent } from './components/user-library/library-page/library.page.component';
import { AddBookComponent } from './components/admin/addBook/addBook.component';
import { HomePageComponent } from './components/home/home-page/home-page.component';
import { UserPageComponent } from './components/Users/user-page/user-page.component';
import { UserSettingsComponent } from './components/Users/user-settings/user-settings-component';
import { UserReservationsComponent } from './components/Users/user-reservations/user-reservations-component'; 
// import { UserMessagesComponent } from './components/Users/user-messages/user-messages.component';
import { UserReadlistComponent } from './components/Users/user-readlist/user-readlist-component';
import { UserCheckoutsComponent } from './components/Users/user-checkouts/user-checkouts-component';
import { AdminPageComponent } from './components/admin/admin-page/admin-page.component';
import { ManageBooksComponent } from './components/admin/manage-books/manage-books.component';
import { ManageUsersComponent } from './components/admin/manage-users/manage-users.component';
import { AuthGuard } from './Services/auth.guard';
import { unsavedChangesGuard } from './Services/unsaved-changes.guard';
import { StartPageComponent } from './components/start-page/start-page.component';
import { MorePageComponent } from './components/more-page/more-page.component';
export const routes: Routes = [
  { path: '', component: StartPageComponent, canActivate: [AuthGuard] }, // Add canActivate here
  {
    path: '',
    canActivateChild: [AuthGuard],
    children: [
      { path: 'home', component: HomePageComponent },
      { path: 'library', component: LibraryComponent },
      // The bottom bar's More tab on small screens
      { path: 'more', component: MorePageComponent },
      { path: 'add-book', component: AddBookComponent, canDeactivate: [unsavedChangesGuard] },
      // Same page in edit mode. Kept outside the admin wrapper so it lays out
      // exactly like Add book; listed before 'admin' so that route doesn't claim it.
      { path: 'admin/manage-books/:id/edit', component: AddBookComponent, canDeactivate: [unsavedChangesGuard] },
      {
        path: 'user-page',
        component: UserPageComponent,
        children: [
          { path: 'settings', component: UserSettingsComponent },
          { path: 'readlist', component: UserReadlistComponent },
          { path: 'reservations', component: UserReservationsComponent },
          { path: 'checkouts', component: UserCheckoutsComponent },
          { path: '', redirectTo: 'settings', pathMatch: 'full' }
        ]
      },
      {
        path: 'admin',
        component: AdminPageComponent,
        children: [
          { path: 'manage-books', component: ManageBooksComponent },
          { path: 'manage-users', component: ManageUsersComponent },
        ]
      },
    ]
  }
];