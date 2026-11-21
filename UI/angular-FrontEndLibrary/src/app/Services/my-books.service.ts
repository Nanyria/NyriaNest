import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResponse, MyBook, SlimBookDto, StatusHistoryItem } from '../Models/interfaces';

// The signed-in user's own books (#83): every call to api/my/books.
// Someone else's book or a shared-library book gives 404.
@Injectable({ providedIn: 'root' })
export class MyBooksService {
  private http = inject(HttpClient);
  private apiUrl = '/api/my/books';

  getAll(): Observable<ApiResponse<MyBook[]>> {
    return this.http.get<ApiResponse<MyBook[]>>(this.apiUrl);
  }

  get(bookId: number): Observable<ApiResponse<MyBook>> {
    return this.http.get<ApiResponse<MyBook>>(`${this.apiUrl}/${bookId}`);
  }

  // Paperbacks and hardcovers only; the result is the new book's id
  add(book: SlimBookDto): Observable<ApiResponse<number>> {
    return this.http.post<ApiResponse<number>>(this.apiUrl, book);
  }

  update(bookId: number, book: SlimBookDto): Observable<ApiResponse<unknown>> {
    return this.http.put<ApiResponse<unknown>>(`${this.apiUrl}/${bookId}`, book);
  }

  // A book lent to someone by name needs confirmLent, or the API answers 409
  delete(bookId: number, confirmLent = false): Observable<ApiResponse<unknown>> {
    return this.http.delete<ApiResponse<unknown>>(`${this.apiUrl}/${bookId}`, { body: { confirmLent } });
  }

  // Newest first
  getHistory(bookId: number): Observable<ApiResponse<StatusHistoryItem[]>> {
    return this.http.get<ApiResponse<StatusHistoryItem[]>>(`${this.apiUrl}/${bookId}/history`);
  }

  // Dates as "yyyy-MM-dd"; neither may be in the future
  lend(bookId: number, borrowerName: string, lentOn: string): Observable<ApiResponse<unknown>> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/${bookId}/lend`, { borrowerName, lentOn });
  }

  markReturned(bookId: number, returnedOn: string): Observable<ApiResponse<unknown>> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/${bookId}/return`, { returnedOn });
  }
}
