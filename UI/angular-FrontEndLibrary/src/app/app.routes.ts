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
  // Resetting a forgotten password (#51), in the start page's card. Open to everyone:
  // they're for people who can't log in (and Settings links to the first one).
  { path: 'forgot-password', component: StartPageComponent, data: { mode: 'forgot' } },
  { path: 'reset-password', component: StartPageComponent, data: { mode: 'reset' } },
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
      // The reader's own pages (the topbar's My books menu), framed by UserPageComponent
      {
        path: 'my',
        component: UserPageComponent,
        children: [
          { path: 'reading-list', component: UserReadlistComponent },
          { path: 'loans', component: UserCheckoutsComponent },
          { path: 'reservations', component: UserReservationsComponent },
          { path: '', redirectTo: 'reading-list', pathMatch: 'full' }
        ]
      },
      // Settings: /settings/profile and /settings/security. One route with a parameter, so
      // switching between them keeps the same page (and any unsaved edits). Phones show both
      // on one scrolling page. Lays itself out, so it isn't framed by UserPageComponent.
      { path: 'settings', redirectTo: 'settings/profile', pathMatch: 'full' },
      {
        path: 'settings/:section',
        component: UserSettingsComponent,
        canDeactivate: [unsavedChangesGuard],
        // Profile <-> Security keeps the page (and edits), so don't ask then; leaving Settings still asks
        runGuardsAndResolvers: () => false,
      },
      // Old My page URLs, so existing links and bookmarks still work
      { path: 'user-page/readlist', redirectTo: '/my/reading-list', pathMatch: 'full' },
      { path: 'user-page/checkouts', redirectTo: '/my/loans', pathMatch: 'full' },
      { path: 'user-page/reservations', redirectTo: '/my/reservations', pathMatch: 'full' },
      { path: 'user-page/settings', redirectTo: '/settings/profile', pathMatch: 'full' },
      { path: 'user-page', redirectTo: '/settings/profile', pathMatch: 'full' },
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