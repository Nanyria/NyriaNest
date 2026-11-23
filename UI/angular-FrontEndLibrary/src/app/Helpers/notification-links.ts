import { NotificationType } from './Enums/enum';

// Where a notification leads when clicked, by its type: NotificationItem has no link of its own.
// null = nowhere to go (the notification is only read). Add types here as their pages exist.
export function notificationLink(type: NotificationType): { route: string; label: string } | null {
  switch (type) {
    case NotificationType.FriendRequestReceived:
    case NotificationType.FriendRequestAccepted:
      return { route: '/friends', label: 'Open Friends' };
    default:
      return null;
  }
}
