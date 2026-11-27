import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResponse, BorrowRequest } from '../Models/interfaces';

// Asking a friend to borrow one of their physical books (#85). The user comes from
// the token. One open request per friend and book; while the book is free several
// friends may ask, and the owner chooses. A lent book is reserved instead
// (UserActionsService.reserveBook).
@Injectable({ providedIn: 'root' })
export class BorrowRequestsService {
  private apiUrl = 'api/borrow-requests';

  constructor(private http: HttpClient) {}

  // The friend asks for a book
  create(bookId: number | string): Observable<ApiResponse<BorrowRequest>> {
    return this.http.post<ApiResponse<BorrowRequest>>(this.apiUrl, { bookId: +bookId });
  }

  // The owner: open requests for my books, longest waiting first
  getIncoming(): Observable<ApiResponse<BorrowRequest[]>> {
    return this.http.get<ApiResponse<BorrowRequest[]>>(`${this.apiUrl}/incoming`);
  }

  // The friend: my open requests, longest waiting first
  getMine(): Observable<ApiResponse<BorrowRequest[]>> {
    return this.http.get<ApiResponse<BorrowRequest[]>>(`${this.apiUrl}/mine`);
  }

  // The owner lends the book (a normal 1-month loan); the other open requests
  // for it become reservations
  accept(id: number): Observable<ApiResponse<BorrowRequest>> {
    return this.http.put<ApiResponse<BorrowRequest>>(`${this.apiUrl}/${id}/accept`, {});
  }

  decline(id: number): Observable<ApiResponse<BorrowRequest>> {
    return this.http.put<ApiResponse<BorrowRequest>>(`${this.apiUrl}/${id}/decline`, {});
  }

  // The friend withdraws their request
  cancel(id: number): Observable<ApiResponse<BorrowRequest>> {
    return this.http.put<ApiResponse<BorrowRequest>>(`${this.apiUrl}/${id}/cancel`, {});
  }

  // An admin cancels it for support (#50); the reason is recorded in the book's history
  adminCancel(id: number, reason: string | null): Observable<ApiResponse<BorrowRequest>> {
    return this.http.put<ApiResponse<BorrowRequest>>(`${this.apiUrl}/${id}/admin-cancel`, { reason });
  }
}
