import {
  Component,
  ElementRef,
  HostListener,
  ChangeDetectionStrategy,
} from '@angular/core';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { filter } from 'rxjs';
import {
  MAIN_NAV,
  NavGroup,
  NavItem,
  isNavGroup,
} from '../nav-items';

// A tab either opens a page or a sheet with a group's links above the bar
interface BottomTab {
  id: string;
  label: string;
  icon: string;
  route?: string;
  activeFor?: string[]; // other pages the tab is highlighted on
  group?: NavGroup;
}

const link = (route: string): NavItem =>
  MAIN_NAV.find((e): e is NavItem => !isNavGroup(e) && e.route === route)!;
const group = (id: string): NavGroup =>
  MAIN_NAV.find((e): e is NavGroup => isNavGroup(e) && e.id === id)!;

const HOME = link('/home');
const LIBRARY = link('/library');
const MY_BOOKS = group('my-books');
const DISCOVER = group('discover');

// Main navigation on small screens: a bar fixed to the bottom of the window
@Component({
  selector: 'app-bottom-nav',
  standalone: true,
  templateUrl: './bottom-nav.component.html',
  styleUrls: ['./bottom-nav.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [RouterModule],
})
export class BottomNavComponent {
  openSheet: string | null = null; // id of the tab whose sheet is open

  readonly tabs: BottomTab[] = [
    { id: 'home', label: HOME.label, icon: HOME.icon, route: HOME.route },
    { id: 'library', label: LIBRARY.label, icon: LIBRARY.icon, route: LIBRARY.route },
    { id: MY_BOOKS.id, label: MY_BOOKS.label, icon: MY_BOOKS.icon, group: MY_BOOKS },
    { id: DISCOVER.id, label: DISCOVER.label, icon: DISCOVER.icon, group: DISCOVER },
    // The pages listed on More, so it stays highlighted on them
    {
      id: 'more',
      label: 'More',
      icon: 'bi-three-dots',
      route: '/more',
      activeFor: ['/admin', '/add-book', '/user-page/settings'],
    },
  ];

  constructor(
    private router: Router,
    private elementRef: ElementRef,
  ) {
    this.router.events
      .pipe(filter((event) => event instanceof NavigationEnd))
      .subscribe(() => (this.openSheet = null));
  }

  get openTab(): BottomTab | undefined {
    return this.tabs.find((tab) => tab.id === this.openSheet);
  }

  isActive(tab: BottomTab): boolean {
    const routes = tab.group
      ? tab.group.items.flatMap((item) => (item.route ? [item.route] : []))
      : [tab.route!, ...(tab.activeFor ?? [])];
    return routes.some((route) => this.router.url.startsWith(route));
  }

  toggleSheet(id: string): void {
    this.openSheet = this.openSheet === id ? null : id;
  }

  // Close the sheet when tapping anywhere outside the bar and the sheet
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.openSheet = null;
    }
  }

  @HostListener('document:keydown.escape')
  onEscape() {
    this.openSheet = null;
  }
}
