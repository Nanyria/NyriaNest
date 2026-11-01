import {
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Output,
  ChangeDetectionStrategy,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../../Services/auth.service';
import { LibraryUiService } from '../../../Services/library-ui.service';
import { ILoggedInUser } from '../../../Models/interfaces';
import {
  ADMIN_NAV,
  MAIN_NAV,
  NavGroup,
  NavItem,
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
  @Output() menuToggle = new EventEmitter<void>();
  currentUser: ILoggedInUser | null = null;
  openMenu: string | null = null; // id of the open dropdown, 'user' for the user menu
  searchQuery = '';

  readonly entries: TopbarEntry[] = MAIN_NAV.map((entry) =>
    isNavGroup(entry)
      ? { kind: 'group', group: entry }
      : { kind: 'link', item: entry },
  );
  readonly adminGroup = ADMIN_NAV;

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
        this.searchQuery = String(this.router.parseUrl(this.router.url).queryParams['q'] ?? '');
      });
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

  // Searching from anywhere opens the library filtered by the term
  search(event: Event, term: string) {
    event.preventDefault();
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

  toggleFilters(): void {
    this.libraryUi.toggleFilters();
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

  @HostListener('document:keydown.escape')
  onEscape() {
    this.openMenu = null;
  }

  logout() {
    this.openMenu = null;
    this.authService.logout();
    this.router.navigate(['']);
  }
}
