//book.services.ts

import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AdminBook,
  ApiResponse,
  EditBookDto,
  CatalogueQuery,
  PagedResult,
  SlimBookDto,
  StatusHistoryItem,
} from '../Models/interfaces';
import { BookStatusEnum } from '../Helpers/Enums/enum';

@Injectable({
  providedIn: 'root',
})
export class BookService {
  private apiUrl = '/api/Book'; // Use relative URL

  constructor(private http: HttpClient) {}

  getBookById(bookID: string): Observable<{ isSuccess: boolean; result: EditBookDto }> {
    return this.http.get<{ isSuccess: boolean; result: EditBookDto }>(`${this.apiUrl}/${bookID}`);
  }

  addBook(book: SlimBookDto): Observable<any> {
    return this.http.post<any>(this.apiUrl, book);
  }

  // Creates an e-book and stores its EPUB in one request (multipart form data)
  addEbook(book: SlimBookDto, file: File): Observable<any> {
    const formData = new FormData();
    Object.entries(book).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        formData.append(key, String(value));
      }
    });
    formData.append('file', file, file.name);
    return this.http.post<any>(`${this.apiUrl}/ebook`, formData);
  }

  updateBook(bookID: string, updatedBook: SlimBookDto): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${bookID}`, updatedBook);
  }

  // Adds an EPUB to an existing e-book, or replaces the one it has
  uploadEpub(bookID: string, file: File): Observable<ApiResponse<string>> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    return this.http.post<ApiResponse<string>>(`${this.apiUrl}/${bookID}/epub`, formData);
  }

  deleteEpub(bookID: string): Observable<ApiResponse<boolean>> {
    return this.http.delete<ApiResponse<boolean>>(`${this.apiUrl}/${bookID}/epub`);
  }

  // Admin catalogue: search, filters, sorting and paging all happen on the server.
  // Empty values are left out so the URL only carries what's set.
  getCatalogue(query: CatalogueQuery): Observable<ApiResponse<PagedResult<AdminBook>>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<ApiResponse<PagedResult<AdminBook>>>(`${this.apiUrl}/catalogue`, { params });
  }

  // Lost/Removed win over loans and reservations until an admin clears them
  setAdminStatus(
    bookId: number,
    status: BookStatusEnum.Lost | BookStatusEnum.Removed,
    notes?: string,
  ): Observable<ApiResponse<unknown>> {
    return this.http.put<ApiResponse<unknown>>(`${this.apiUrl}/${bookId}/admin-status`, { status, notes });
  }

  clearAdminStatus(bookId: number): Observable<ApiResponse<unknown>> {
    return this.http.delete<ApiResponse<unknown>>(`${this.apiUrl}/${bookId}/admin-status`);
  }

  deleteBook(bookID: string): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${bookID}`);
  }

  getBookHistory(bookID: string): Observable<{ isSuccess: boolean; result: StatusHistoryItem[] }> {
    return this.http.get<{ isSuccess: boolean; result: StatusHistoryItem[] }>(
      `${this.apiUrl}/history/${bookID}`
    );
  }
}
