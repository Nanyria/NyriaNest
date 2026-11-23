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
import { MyOwnedBooksComponent } from './components/Users/my-owned-books/my-owned-books.component';
import { AdminPageComponent } from './components/admin/admin-page/admin-page.component';
import { ManageBooksComponent } from './components/admin/manage-books/manage-books.component';
import { ManageUsersComponent } from './components/admin/manage-users/manage-users.component';
import { AuthGuard } from './Services/auth.guard';
import { unsavedChangesGuard } from './Services/unsaved-changes.guard';
import { StartPageComponent } from './components/start-page/start-page.component';
import { MorePageComponent } from './components/more-page/more-page.component';
import { NotificationsPageComponent } from './components/Users/notifications-page/notifications-page.component';
import { FriendsPageComponent } from './components/Users/friends-page/friends-page.component';
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
      // Every notification, read and unread; the topbar's bell links here (#91)
      { path: 'notifications', component: NotificationsPageComponent },
      // Friends, friend requests and the sharing settings per friend (#84)
      { path: 'friends', component: FriendsPageComponent },
      // The add/edit book page. Where it's opened from decides where the book goes
      // (data.area, #83): 'my' = the user's own books, 'admin' = Manage books, where
      // adding puts an e-book in the shared library. Kept outside the 'my' and 'admin'
      // wrappers so all four lay out the same; listed first so those routes don't claim them.
      { path: 'my/owned-books/add', component: AddBookComponent, data: { area: 'my' }, canDeactivate: [unsavedChangesGuard] },
      { path: 'my/owned-books/:id/edit', component: AddBookComponent, data: { area: 'my' }, canDeactivate: [unsavedChangesGuard] },
      { path: 'admin/manage-books/add', component: AddBookComponent, data: { area: 'admin' }, canDeactivate: [unsavedChangesGuard] },
      { path: 'admin/manage-books/:id/edit', component: AddBookComponent, data: { area: 'admin' }, canDeactivate: [unsavedChangesGuard] },
      // Old URL, so links and bookmarks still work: adding your own book is open to everyone
      { path: 'add-book', redirectTo: '/my/owned-books/add', pathMatch: 'full' },
      // The reader's own pages (the topbar's My books menu), framed by UserPageComponent
      {
        path: 'my',
        component: UserPageComponent,
        children: [
          // The books on the user's own shelf and who has borrowed them (#83)
          { path: 'owned-books', component: MyOwnedBooksComponent },
          { path: 'reading-list', component: UserReadlistComponent },
          { path: 'loans', component: UserCheckoutsComponent },
          { path: 'reservations', component: UserReservationsComponent },
          { path: '', redirectTo: 'owned-books', pathMatch: 'full' }
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