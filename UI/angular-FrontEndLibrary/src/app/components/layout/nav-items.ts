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
  { label: 'Home', icon: 'bi-house-door', route: '/home' },
  { label: 'Library', icon: 'bi-book', route: '/library' },
  {
    id: 'discover',
    label: 'Discover',
    icon: 'bi-compass',
    items: [
      { label: 'Recommendations', icon: 'bi-stars' },
      { label: 'Reviews', icon: 'bi-chat-left-text' },
    ],
  },
  { label: 'Challenges', icon: 'bi-award' },
  {
    id: 'my-books',
    label: 'My books',
    icon: 'bi-journal-bookmark',
    items: [
      { label: 'Reading list', icon: 'bi-heart', route: '/user-page/readlist' },
      { label: 'Borrowed books', icon: 'bi-book-half', route: '/user-page/checkouts' },
      { label: 'Reservations', icon: 'bi-bookmark', route: '/user-page/reservations' },
    ],
  },
];

export const ADMIN_NAV: NavGroup = {
  id: 'admin',
  label: 'Admin',
  icon: 'bi-shield-lock',
  items: [
    { label: 'Dashboard', icon: 'bi-speedometer2' },
    { label: 'Manage books', icon: 'bi-journals', route: '/admin/manage-books' },
    { label: 'Add book', icon: 'bi-journal-plus', route: '/add-book' },
    { label: 'Manage users', icon: 'bi-people', route: '/admin/manage-users' },
  ],
};
