import {
  Component,
  ElementRef,
  HostListener,
  ChangeDetectionStrategy,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../../Services/auth.service';
import { LibraryUiService } from '../../../Services/library-ui.service';
import { ILoggedInUser } from '../../../Models/interfaces';
import { DEFAULT_PROFILE_PICTURE } from '../../../Helpers/profile-pictures';
import {
  ADMIN_NAV,
  MAIN_NAV,
  NavGroup,
  NavItem,
  SETTINGS_NAV,
  isNavGroup,
} from '../nav-items';

type TopbarEntry =
  | { kind: 'link'; item: NavItem }
  | { kind: 'group'; group: NavGroup };

@Component({
  selector: 'app-topbar',
  standalone: true,
  templateUrl: './topbar.component.html',
  styleUrls: ['./topbar.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [RouterModule, NgTemplateOutlet],
})
export class TopbarComponent {
  currentUser: ILoggedInUser | null = null;
  openMenu: string | null = null; // id of the open dropdown, 'user' for the user menu
  searchQuery = '';
  searchOpen = false; // small screens: the search field covers the bar
  isScrolled = window.scrollY > 0; // phones: clear at the top, frosted below it

  // No Home link here: the logo leads home
  readonly entries: TopbarEntry[] = MAIN_NAV.filter(
    (entry) => isNavGroup(entry) || entry.route !== '/home',
  ).map((entry) =>
    isNavGroup(entry)
      ? { kind: 'group', group: entry }
      : { kind: 'link', item: entry },
  );
  readonly adminGroup = ADMIN_NAV;
  readonly defaultPicture = DEFAULT_PROFILE_PICTURE;

  constructor(
    private authService: AuthService,
    private libraryUi: LibraryUiService,
    private router: Router,
    private elementRef: ElementRef,
  ) {
    this.searchQuery = String(this.router.parseUrl(this.router.url).queryParams['q'] ?? '');
    this.authService.getCurrentUser().subscribe((user) => {
      this.currentUser = user;
    });

    this.router.events
      .pipe(filter((event) => event instanceof NavigationEnd))
      .subscribe(() => {
        this.openMenu = null;
        this.searchOpen = false;
        this.searchQuery = String(this.router.parseUrl(this.router.url).queryParams['q'] ?? '');
      });
  }

  // A picture that can't be loaded (removed, or a deleted upload) shows the default
  // instead of a broken image. Only once, so a missing default can't loop.
  onAvatarError(event: Event): void {
    const img = event.target as HTMLImageElement;
    if (!img.src.endsWith(this.defaultPicture)) img.src = this.defaultPicture;
  }

  get hasNewNotification(): boolean {
    return !!this.currentUser?.notifications?.some((n) => !n.isRead);
  }

  get isAdmin(): boolean {
    return !!this.currentUser?.adminRole || !!this.currentUser?.isSuperAdmin;
  }

  // A dropdown is highlighted while one of its pages is open
  isGroupActive(group: NavGroup): boolean {
    return group.items.some(
      (item) => !!item.route && this.router.url.startsWith(item.route),
    );
  }

  // The user menu too: its only page so far is Settings
  get isUserMenuActive(): boolean {
    return this.router.url.startsWith(SETTINGS_NAV.route!);
  }

  // Searching from anywhere opens the library filtered by the term
  search(event: Event, term: string) {
    event.preventDefault();
    this.searchOpen = false;
    this.router.navigate(['/library'], {
      queryParams: { q: term.trim() || null },
    });
  }

  // The Filters button only makes sense on the library page
  get isLibraryPage(): boolean {
    return this.router.url.startsWith('/library');
  }

  // Admin pages have their own search, so the library search is hidden there
  get isAdminPage(): boolean {
    return this.router.url.startsWith('/admin');
  }

  // Focus after the field is shown; it can't take focus while hidden
  openSearch(input: HTMLInputElement): void {
    this.searchOpen = true;
    setTimeout(() => input.focus());
  }

  get filtersOpen(): boolean {
    return this.libraryUi.filtersOpen();
  }

  // On Library the panel opens in place; elsewhere Library opens with it,
  // keeping what's typed in the search
  openFilters(): void {
    if (this.isLibraryPage) {
      this.libraryUi.toggleFilters();
      return;
    }
    this.router.navigate(['/library'], {
      queryParams: { q: this.searchQuery.trim() || null, filters: 'open' },
    });
  }

  toggleMenu(id: string) {
    this.openMenu = this.openMenu === id ? null : id;
  }

  // Close any open menu when clicking anywhere outside the topbar
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.openMenu = null;
    }
  }

  @HostListener('window:scroll')
  onScroll() {
    this.isScrolled = window.scrollY > 0;
  }

  @HostListener('document:keydown.escape')
  onEscape() {
    this.openMenu = null;
    this.searchOpen = false;
  }

  logout() {
    this.openMenu = null;
    this.authService.logout();
    this.router.navigate(['']);
  }
}
