import { Component, ChangeDetectionStrategy, HostListener } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { FriendsService, SharingSetting } from '../../../Services/friends.service';
import { FriendshipItem, FriendsOverview } from '../../../Models/interfaces';
import { DEFAULT_PROFILE_PICTURE } from '../../../Helpers/profile-pictures';
import { ConfirmService } from '../../../Services/confirm.service';
import { ToastService } from '../../../Services/toast.service';

// Friends and friend requests, and what the user shares with each friend (#84).
// Everything comes from one overview call, loaded again after each change so the
// "7 of 10" counts always match the server.
@Component({
  selector: 'app-friends-page',
  standalone: true,
  templateUrl: './friends-page.component.html',
  styleUrls: ['./friends-page.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [DatePipe, NgTemplateOutlet, RouterLink],
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
  }

  // Only the first load shows "Loading…"; reloads after a change keep the page in place
  load(): void {
    if (!this.overview) this.state = 'loading';
    this.friendsService.getOverview().subscribe({
      next: (response) => {
        this.overview = response.result;
        this.state = 'ready';
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
