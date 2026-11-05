import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateReviewItemDto, CreateRatingItemDto, ReviewItemDto, RatingItemDto } from '../Models/interfaces';
import { CreateUserDto } from '../Models/interfaces';

// Every action applies to the logged-in user: the API takes the user from the
// token the auth interceptor sends, so no user ID goes in the URL.
@Injectable({
  providedIn: 'root',
})
export class UserActionsService {
private apiUrl = 'api/UserActions'; // Base URL for the User API

  constructor(private http: HttpClient) {}
  // Add a new user
  addUser(user: CreateUserDto): Observable<any> {
    return this.http.post<any>(this.apiUrl, user);
  }
  // Borrow a book
  borrowBook(bookId: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/borrow/${bookId}`, {});
  }

  // Reserve a book
  reserveBook(bookId: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/reserve/${bookId}`, {});
  }

  // Unreserve a book
  unreserveBook(bookId: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/unreserve/${bookId}`, {});
  }

  // Return a book
  returnBook(bookId: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/return/${bookId}`, {});
  }

  // To add - list of borrowed books
  getBorrowedBooks(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/checkedout`);
  }

  // To add - list of reserved books
  getReservedBooks(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/reserved`);
  }

  // Reviews
  getUserReviews(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/reviews`);
  }

  addReview(review: CreateReviewItemDto): Observable<any> {
  return this.http.post<any>(`${this.apiUrl}/newReview`, review);
  }

  editReview(reviewId: string, review: ReviewItemDto): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/editReview/${reviewId}`, review);
  }
  deleteReview(reviewId: string): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/deleteReview/${reviewId}`);
  }
  // Ratings
  editRating(reviewId: string, rating: RatingItemDto): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/editRating/${reviewId}`, rating);
  }

  deleteRating(reviewId: string): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/deleteRating/${reviewId}`);
  }

  // Favorites
  getUserFavorites(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/favorites`);
  }
  addToFavorites(bookId: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/favorite/${bookId}`, {});
  }
  removeFromFavorites(bookId: string): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/unfavorite/${bookId}`);
  }
  // Still sends the user ID: fixed with security finding 2 (#48)
  updateUser(userId: string, user: any): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/update/${userId}`, user);
  }
}
