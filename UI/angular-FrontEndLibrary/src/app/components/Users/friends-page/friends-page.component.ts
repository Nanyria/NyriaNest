import { Component, ChangeDetectionStrategy, HostListener } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, debounceTime, distinctUntilChanged, map, of, switchMap, tap } from 'rxjs';
import { FriendsService, SharingSetting } from '../../../Services/friends.service';
import { FriendshipItem, FriendsOverview, UserSearchResult } from '../../../Models/interfaces';
import { DEFAULT_PROFILE_PICTURE } from '../../../Helpers/profile-pictures';
import { FriendshipStatus } from '../../../Helpers/Enums/enum';
import { ConfirmService } from '../../../Services/confirm.service';
import { ToastService } from '../../../Services/toast.service';

// One row in the search results: a friend, a request either way, or someone new
interface SearchRow {
  key: string; // user id
  userName: string;
  picture: string | null;
  kind: 'friend' | 'received' | 'sent' | 'stranger';
  friendship?: FriendshipItem; // not for strangers
}

// Friends and friend requests, and what the user shares with each friend (#84).
// Everything comes from one overview call, loaded again after each change so the
// "7 of 10" counts always match the server.
@Component({
  selector: 'app-friends-page',
  standalone: true,
  templateUrl: './friends-page.component.html',
  styleUrls: ['./friends-page.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [DatePipe, NgTemplateOutlet, RouterLink, ReactiveFormsModule],
})
export class FriendsPageComponent {
  overview: FriendsOverview | null = null;
  state: 'loading' | 'error' | 'ready' = 'loading';
  readonly defaultPicture = DEFAULT_PROFILE_PICTURE;

  constructor(
    private friendsService: FriendsService,
    private confirmDialog: ConfirmService,
    private toast: ToastService,
  ) {
    this.load();
    this.listenToSearch();
  }

  // ---------- Search for friends: one bar for friends, requests and new people ----------
  // Friends and open requests come from the overview (the API search leaves them out);
  // everyone else comes from the API. The same text also filters "Your friends".
  readonly minSearchLength = 2;
  readonly search = new FormControl('', { nonNullable: true });
  // The text being searched for, after the pause in typing; '' below the minimum
  searchTerm = '';
  // State of the API part only: known people show at once
  searchState: 'idle' | 'loading' | 'ready' | 'error' = 'idle';
  private strangers: UserSearchResult[] = [];

  // Same rule as the API: user name starts with the text, any case
  private matches(userName: string): boolean {
    return userName.toLowerCase().startsWith(this.searchTerm.toLowerCase());
  }

  // Everyone the search found, by name. Built from the overview each time, so a reload
  // after Accept (or later Add) moves the person to their new status.
  get searchResults(): SearchRow[] {
    if (!this.searchTerm || !this.overview) return [];
    const o = this.overview;
    const known = (items: FriendshipItem[], kind: SearchRow['kind']): SearchRow[] =>
      items.filter((f) => this.matches(f.friendUserName)).map((f) => ({
        key: f.friendUserId,
        userName: f.friendUserName,
        picture: f.friendProfilePictureUrl,
        kind,
        friendship: f,
      }));
    const rows = [
      ...known(o.friends, 'friend'),
      ...known(o.received, 'received'),
      ...known(o.sent, 'sent'),
      ...this.strangers.map((u): SearchRow => ({ key: u.userId, userName: u.userName, picture: null, kind: 'stranger' })),
    ];
    return rows.sort((a, b) => a.userName.localeCompare(b.userName));
  }

  // The dropdown under the header's search field: open while the search has focus and
  // something is typed; Escape or leaving the search (click or Tab elsewhere) closes it
  searchOpen = false;

  get showSearchPanel(): boolean {
    return this.searchOpen && this.search.value.trim().length > 0;
  }

  openSearch(): void {
    this.searchOpen = true;
  }

  closeSearch(): void {
    this.searchOpen = false;
  }

  // Focus moving to something inside the search (e.g. an Accept button) keeps it open
  onSearchFocusOut(event: FocusEvent): void {
    const wrapper = event.currentTarget as HTMLElement;
    if (!wrapper.contains(event.relatedTarget as Node | null)) this.closeSearch();
  }

  // ---------- Add: send a friend request to someone new from the search results ----------
  // The user waiting for the server, so the button can't be clicked twice
  busyAdd: string | null = null;

  addFriend(row: SearchRow, searchInput: HTMLInputElement): void {
    // The button is disabled now and goes away after the reload: move the focus back to the
    // search first, so the dropdown stays open and Esc / clicking outside still close it
    searchInput.focus();
    this.busyAdd = row.key;
    this.friendsService.sendRequest(row.userName).subscribe({
      next: (res) => {
        // If they had asked us in the meantime, the server accepts their request instead
        // (one row per pair); the reload then shows them as "✓ Friends"
        const nowFriends = res.result?.status === FriendshipStatus.Accepted;
        this.toast.success(nowFriends
          ? `You're now friends with ${row.userName}.`
          : `Friend request sent to ${row.userName}.`);
        // Once the overview has the request, the person shows as "Request sent" (from the overview),
        // so they leave the API results at that same moment: no gap where the row disappears
        this.load(() => {
          this.strangers = this.strangers.filter((u) => u.userId !== row.key);
          this.busyAdd = null;
        });
      },
      error: () => {
        this.busyAdd = null;
        this.toast.error(`Couldn't send a friend request to ${row.userName}. Please try again.`);
      },
    });
  }

  // "Your friends" narrowed by the search; all of them when the search is empty
  get shownFriends(): FriendshipItem[] {
    const friends = this.overview?.friends ?? [];
    return this.searchTerm ? friends.filter((f) => this.matches(f.friendUserName)) : friends;
  }

  // Waits for a pause in typing; switchMap drops an older answer when a newer search starts
  private listenToSearch(): void {
    this.search.valueChanges
      .pipe(
        map((text) => text.trim()),
        debounceTime(300),
        distinctUntilChanged(),
        tap((text) => {
          const searching = text.length >= this.minSearchLength;
          this.searchTerm = searching ? text : '';
          this.strangers = [];
          this.searchState = searching ? 'loading' : 'idle';
        }),
        switchMap((text) => {
          if (text.length < this.minSearchLength) return of(null);
          return this.friendsService.search(text).pipe(
            map((response) => response.result ?? []),
            catchError(() => of('error' as const)),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((results) => {
        if (results === 'error') {
          this.searchState = 'error';
        } else if (results !== null) {
          this.strangers = results;
          this.searchState = 'ready';
        }
      });
  }

  // Only the first load shows "Loading…"; reloads after a change keep the page in place
  // afterLoad: runs once the new overview is in (e.g. to update the search results at the same moment)
  load(afterLoad?: () => void): void {
    if (!this.overview) this.state = 'loading';
    this.friendsService.getOverview().subscribe({
      next: (response) => {
        this.overview = response.result;
        this.state = 'ready';
        afterLoad?.();
      },
      error: () => (this.state = 'error'),
    });
  }

  // ---------- The two limit cards ----------
  // At most this many faces on a limit card; the rest are a "+3"
  readonly maxFaces = 4;

  // The friends I've turned this setting on for, in the list's order (by name)
  sharingWith(setting: SharingSetting): FriendshipItem[] {
    const friends = this.overview?.friends ?? [];
    return friends.filter((f) => (setting === 'ebooks' ? f.iShareEbooks : f.iSharePhysicalLibrary));
  }

  // How full the bar is, 0-100. At least a sliver when something is shared, so 1 of 100 shows.
  barPercent(used: number, limit: number): number {
    if (limit <= 0 || used <= 0) return 0;
    return Math.min(100, Math.max(2, (used / limit) * 100));
  }

  // ---------- Friend cards: collapsed to a summary, "Sharing" opens the switches ----------
  // Kept by friendship id, so a card stays open through the reload after a change
  private openCards = new Set<number>();

  isOpen(friend: FriendshipItem): boolean {
    return this.openCards.has(friend.id);
  }

  toggleCard(friend: FriendshipItem): void {
    if (!this.openCards.delete(friend.id)) this.openCards.add(friend.id);
  }

  // The sentence behind one of the four icons, as tooltip and for screen readers,
  // e.g. "You share your physical books with boris.books" / "boris.books doesn't share their e-books with you"
  shareLabel(friend: FriendshipItem, side: 'mine' | 'theirs', kind: 'physical' | 'ebooks'): string {
    const name = friend.friendUserName;
    const what = kind === 'physical' ? 'physical books' : 'e-books';
    if (side === 'mine') {
      const on = kind === 'physical' ? friend.iSharePhysicalLibrary : friend.iShareEbooks;
      return on ? `You share your ${what} with ${name}` : `You don't share your ${what} with ${name}`;
    }
    const on = kind === 'physical' ? friend.friendSharesPhysicalLibrary : friend.friendSharesEbooks;
    return on ? `${name} shares their ${what} with you` : `${name} doesn't share their ${what} with you`;
  }

  // The switch waiting for the server, e.g. "12:ebooks", so it can't be clicked twice
  busySwitch: string | null = null;

  isBusy(friend: FriendshipItem, setting: SharingSetting): boolean {
    return this.busySwitch === `${friend.id}:${setting}`;
  }

  // Turns one of my sharing settings for this friend on or off, then reloads so the
  // switches and the "7 of 10" counts show what the server has
  setSharing(friend: FriendshipItem, setting: SharingSetting, input: HTMLInputElement): void {
    const on = input.checked;
    this.busySwitch = `${friend.id}:${setting}`;
    this.sharingError = null;
    this.friendsService.setSharing(friend.id, setting, on).subscribe({
      next: () => this.afterSharingChange(),
      error: (err: HttpErrorResponse) => {
        // The reloaded value is the same as before, so Angular won't move the switch back itself
        input.checked = !on;
        // Over the limit, the API's message names it ("…with 10 friends, which is the limit…")
        const message = err.error?.errorMessages?.[0] ?? "Couldn't change that setting. Please try again.";
        this.sharingError = { friendId: friend.id, message };
        this.afterSharingChange();
      },
    });
  }

  // The last refused change, shown on that friend's card until the next change
  sharingError: { friendId: number; message: string } | null = null;

  errorFor(friend: FriendshipItem): string | null {
    return this.sharingError?.friendId === friend.id ? this.sharingError.message : null;
  }

  // ---------- The ⋮ menu on each card (same popover as My owned books) ----------
  placeMenu(event: Event, button: HTMLElement): void {
    if ((event as ToggleEvent).newState !== 'open') return;
    const menu = event.target as HTMLElement;
    const r = button.getBoundingClientRect();
    const openUp = r.bottom > window.innerHeight - 120;
    menu.style.right = `${document.documentElement.clientWidth - r.right}px`;
    menu.style.top = openUp ? 'auto' : `${r.bottom + 4}px`;
    menu.style.bottom = openUp ? `${window.innerHeight - r.top + 4}px` : 'auto';
  }

  closeMenu(event: Event): void {
    (event.currentTarget as HTMLElement).hidePopover();
  }

  // The menu is placed once when it opens, so scrolling or resizing closes it
  @HostListener('window:scroll')
  @HostListener('window:resize')
  closeOpenMenus(): void {
    document.querySelectorAll<HTMLElement>('.friends__menu:popover-open').forEach((m) => m.hidePopover());
  }

  // ---------- Remove (from the ⋮ menu) ----------
  // Ends the friendship for both of us: the row goes, with all four settings
  async remove(friend: FriendshipItem): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: `Remove ${friend.friendUserName}?`,
      message: 'Sharing stops both ways, and you can no longer see each other\'s books.',
      confirmLabel: 'Remove friend',
      danger: true,
      icon: 'person-dash',
    });
    if (!confirmed) return;
    this.sharingError = null;
    this.friendsService.remove(friend.id).subscribe({
      next: () => {
        this.toast.success(`${friend.friendUserName} is no longer your friend.`);
        this.load();
      },
      error: () => {
        this.toast.error(`Couldn't remove ${friend.friendUserName}. Please try again.`);
        this.load();
      },
    });
  }

  // ---------- Requests ----------
  // Received first (they need an answer), then sent. The panel shows one at a time,
  // with ‹ › to move through them, so it stays as tall as the limit cards beside it.
  private requestPosition = 0;

  // Kept in range when the list gets shorter (e.g. after accepting the last one)
  get requestIndex(): number {
    return Math.min(this.requestPosition, Math.max(0, this.requestRows.length - 1));
  }

  showRequest(index: number): void {
    this.requestPosition = Math.max(0, Math.min(index, this.requestRows.length - 1));
  }

  get requestRows(): { item: FriendshipItem; received: boolean }[] {
    const o = this.overview;
    if (!o) return [];
    return [
      ...o.received.map((item) => ({ item, received: true })),
      ...o.sent.map((item) => ({ item, received: false })),
    ];
  }

  get shownRequests(): { item: FriendshipItem; received: boolean }[] {
    const rows = this.requestRows;
    return rows.slice(this.requestIndex, this.requestIndex + 1);
  }

  // The request waiting for the server, so its buttons can't be clicked twice
  busyRequest: number | null = null;

  accept(request: FriendshipItem): void {
    this.answer(request, this.friendsService.accept(request.id),
      `You and ${request.friendUserName} are now friends.`,
      `Couldn't accept the request from ${request.friendUserName}. Please try again.`);
  }

  decline(request: FriendshipItem): void {
    this.answer(request, this.friendsService.decline(request.id),
      `Request from ${request.friendUserName} declined.`,
      `Couldn't decline the request from ${request.friendUserName}. Please try again.`);
  }

  cancel(request: FriendshipItem): void {
    this.answer(request, this.friendsService.cancel(request.id),
      `Request to ${request.friendUserName} cancelled.`,
      `Couldn't cancel the request to ${request.friendUserName}. Please try again.`);
  }

  // Accept, decline and cancel all end the same way: a toast, then a reload
  // (e.g. the other person may have cancelled meanwhile, which gives a 404)
  private answer(request: FriendshipItem, call: Observable<unknown>, done: string, failed: string): void {
    this.busyRequest = request.id;
    this.sharingError = null;
    call.subscribe({
      next: () => {
        this.busyRequest = null;
        this.toast.success(done);
        this.load();
      },
      error: () => {
        this.busyRequest = null;
        this.toast.error(failed);
        this.load();
      },
    });
  }

  // A picture that can't be loaded (e.g. a deleted upload) falls back to the default, like the topbar
  onAvatarError(event: Event): void {
    const img = event.target as HTMLImageElement;
    if (!img.src.endsWith(this.defaultPicture)) img.src = this.defaultPicture;
  }

  private afterSharingChange(): void {
    this.busySwitch = null;
    this.load();
  }
}
