
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { LibraryBook } from '../Models/interfaces';


@Injectable({
  providedIn: 'root',
})
export class LibraryService {
  private apiUrl = 'api/Library';

  constructor(private http: HttpClient) {}

getAllBooks(): Observable<{ isSuccess: boolean; result: LibraryBook[] }> {
  return this.http.get<{ isSuccess: boolean; result: LibraryBook[] }>(this.apiUrl);
}

    getBookReviews(bookId: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/review/${bookId}`);
  }
}
