import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateReviewItemDto, CreateRatingItemDto, ReviewItemDto, RatingItemDto } from '../Models/interfaces';
import { CreateUserDto } from '../Models/interfaces';
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
  borrowBook(userId: string, bookId: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/borrow/${userId}/${bookId}`, {});
  }

  // Reserve a book
  reserveBook(userId: string, bookId: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/reserve/${userId}/${bookId}`, {});
  }

  // Unreserve a book
  unreserveBook(userId: string, bookId: string): Observable<any> {
    return this.http.put<any>(
      `${this.apiUrl}/unreserve/${userId}/${bookId}`,
      {},
    );
  }

  // Return a book
  returnBook(userId: string, bookId: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/return/${userId}/${bookId}`, {});
  }

  // To add - list of borrowed books
  getBorrowedBooks(userId: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/checkedout/${userId}`);
  }

  // To add - list of reserved books
  getReservedBooks(userId: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/reserved/${userId}`);
  }

  // Reviews
  getUserReviews(userId: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/reviews/${userId}`); 
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
  getUserFavorites(userId: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/favorites/${userId}`);
  }
  addToFavorites(userId: string, bookId: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/favorite/${userId}/${bookId}`, {});
  }
  removeFromFavorites(userId: string, bookId: string): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/unfavorite/${userId}/${bookId}`);
  }
  updateUser(userId: string, user: any): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/update/${userId}`, user);
  }
}
