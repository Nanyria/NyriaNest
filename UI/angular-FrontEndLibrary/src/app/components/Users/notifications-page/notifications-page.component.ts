import { Component, ChangeDetectionStrategy } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NotificationService } from '../../../Services/notification.service';
import { ToastService } from '../../../Services/toast.service';
import { NotificationItem, PagedResult } from '../../../Models/interfaces';

// Every notification the user has, read and unread, newest first (#91).
// The topbar's bell shows only the newest unread and links here with "See all".
@Component({
  selector: 'app-notifications-page',
  standalone: true,
  templateUrl: './notifications-page.component.html',
  styleUrls: ['./notifications-page.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [DatePipe],
})
export class NotificationsPageComponent {
  readonly pageSize = 20;
  page: PagedResult<NotificationItem> | null = null;
  state: 'loading' | 'error' | 'ready' = 'loading';

  constructor(
    private notificationService: NotificationService,
    private toast: ToastService,
  ) {
    this.load(1);
  }

  load(pageNumber: number): void {
    this.state = 'loading';
    this.notificationService.getNotifications(false, pageNumber, this.pageSize).subscribe({
      next: (response) => {
        this.page = response.result;
        this.state = 'ready';
      },
      error: () => (this.state = 'error'),
    });
  }

  get totalPages(): number {
    return this.page ? Math.max(1, Math.ceil(this.page.totalCount / this.page.pageSize)) : 1;
  }

  get hasUnread(): boolean {
    return !!this.page?.items.some((item) => !item.isRead);
  }

  markAsRead(item: NotificationItem): void {
    if (item.isRead) return;
    this.notificationService.markAsRead(item.id).subscribe({
      next: () => (item.isRead = true),
      error: () => this.toast.error("Couldn't mark the notification as read"),
    });
  }

  // Marks every unread notification, also those on other pages
  markAllAsRead(): void {
    this.notificationService.markAllAsRead().subscribe({
      next: () => this.page?.items.forEach((item) => (item.isRead = true)),
      error: () => this.toast.error("Couldn't mark the notifications as read"),
    });
  }
}
