// Main navigation, shared by the topbar (desktop) and the drawer (small screens)

export interface NavItem {
  label: string;
  icon: string;
  route?: string; // No route = placeholder for a feature without backend support
}

export interface NavGroup {
  id: string;
  label: string;
  icon: string;
  items: NavItem[];
}

export type NavEntry = NavItem | NavGroup;

export const isNavGroup = (entry: NavEntry): entry is NavGroup =>
  'items' in entry;

export const MAIN_NAV: NavEntry[] = [
  { label: 'Home', icon: 'nn-icon-home', route: '/home' },
  { label: 'Library', icon: 'nn-icon-library', route: '/library' },
  {
    id: 'discover',
    label: 'Discover',
    icon: 'nn-icon-explore',
    items: [
      { label: 'Recommendations', icon: 'bi-stars' },
      { label: 'Reviews', icon: 'bi-chat-left-text' },
    ],
  },
  { label: 'Challenges', icon: 'nn-icon-challenges' },
  {
    id: 'my-books',
    label: 'My books',
    icon: 'nn-icon-mybooks',
    items: [
      { label: 'Reading list', icon: 'bi-heart', route: '/my/reading-list' },
      { label: 'Borrowed books', icon: 'bi-book-half', route: '/my/loans' },
      { label: 'Reservations', icon: 'bi-bookmark', route: '/my/reservations' },
    ],
  },
];

export const ADMIN_NAV: NavGroup = {
  id: 'admin',
  label: 'Admin',
  icon: 'nn-icon-admin',
  items: [
    { label: 'Dashboard', icon: 'bi-speedometer2' },
    { label: 'Manage books', icon: 'bi-journals', route: '/admin/manage-books' },
    { label: 'Add book', icon: 'bi-journal-plus', route: '/add-book' },
    { label: 'Manage users', icon: 'bi-people', route: '/admin/manage-users' },
  ],
};

export const SETTINGS_NAV: NavItem = {
  label: 'Settings',
  icon: 'bi-gear',
  route: '/settings',
};

// Small screens: Home and Library have their own tabs in the bottom bar, the
// other plain links (Challenges so far) go on the More page
const BOTTOM_TAB_ROUTES = ['/home', '/library'];
export const MORE_LINKS: NavItem[] = MAIN_NAV.filter(
  (entry): entry is NavItem =>
    !isNavGroup(entry) && !BOTTOM_TAB_ROUTES.includes(entry.route ?? ''),
);
