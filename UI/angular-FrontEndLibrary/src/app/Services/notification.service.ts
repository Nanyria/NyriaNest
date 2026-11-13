import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { NavigationEnd, Router } from '@angular/router';
import { EMPTY, Observable, Subject, filter, fromEvent, merge, switchMap, tap, catchError } from 'rxjs';
import { ApiResponse, NotificationItem, PagedResult } from '../Models/interfaces';
import { AuthService } from './auth.service';

// The logged-in user's notifications (#91). The user comes from the token.
// Keeps the unread count for the topbar's bell, refreshed when the app loads,
// on every navigation, after marking something read and when the tab is shown
// again. No timed polling; real-time push is #129.
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private apiUrl = 'api/notifications';
  private loggedIn = false;
  private readonly refresh$ = new Subject<void>();

  private readonly unreadCountSignal = signal(0);
  readonly unreadCount = this.unreadCountSignal.asReadonly();

  constructor(
    private http: HttpClient,
    private authService: AuthService,
    private router: Router,
  ) {
    // switchMap: a newer refresh cancels one still on its way, so an old count can't win
    this.refresh$
      .pipe(
        filter(() => this.loggedIn),
        switchMap(() =>
          this.http
            .get<ApiResponse<number>>(`${this.apiUrl}/unread-count`)
            .pipe(catchError(() => EMPTY)), // keep the last count if the request fails
        ),
      )
      .subscribe((response) => {
        if (this.loggedIn && response.isSuccess) this.unreadCountSignal.set(response.result);
      });

    // Logging in (and app load with a saved login) refreshes; logging out clears the bell
    this.authService.getAuthStatus().subscribe((loggedIn) => {
      this.loggedIn = loggedIn;
      if (loggedIn) this.refreshUnreadCount();
      else this.unreadCountSignal.set(0);
    });

    merge(
      this.router.events.pipe(filter((event) => event instanceof NavigationEnd)),
      fromEvent(document, 'visibilitychange').pipe(filter(() => document.visibilityState === 'visible')),
    ).subscribe(() => this.refreshUnreadCount());
  }

  refreshUnreadCount(): void {
    this.refresh$.next();
  }

  // Newest first. The API keeps pageSize between 1 and 50.
  getNotifications(
    unreadOnly = false,
    page = 1,
    pageSize = 10,
  ): Observable<ApiResponse<PagedResult<NotificationItem>>> {
    const params = new HttpParams()
      .set('unreadOnly', unreadOnly)
      .set('page', page)
      .set('pageSize', pageSize);
    return this.http.get<ApiResponse<PagedResult<NotificationItem>>>(this.apiUrl, { params });
  }

  // Someone else's or an unknown id gives a 404
  markAsRead(id: number): Observable<ApiResponse<NotificationItem>> {
    return this.http
      .put<ApiResponse<NotificationItem>>(`${this.apiUrl}/${id}/read`, {})
      .pipe(tap(() => this.refreshUnreadCount()));
  }

  // The result is how many notifications changed
  markAllAsRead(): Observable<ApiResponse<number>> {
    return this.http
      .put<ApiResponse<number>>(`${this.apiUrl}/read-all`, {})
      .pipe(tap(() => this.refreshUnreadCount()));
  }
}
