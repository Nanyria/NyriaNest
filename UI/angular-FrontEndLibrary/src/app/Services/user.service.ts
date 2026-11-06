import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { UserDto } from '../Models/interfaces';

@Injectable({
  providedIn: 'root',
})
export class UserService {
private apiUrl = 'api/User'; // Base URL for the User API

  constructor(private http: HttpClient) {}

  // Get all users
  getAllUsers(): Observable<{ isSuccess: boolean; result: UserDto[] }> {
    return this.http.get<{ isSuccess: boolean; result: UserDto[] }>(this.apiUrl);
  }

  // Get user by ID (admin only)
  getUserById(userId: string): Observable<{ isSuccess: boolean; result: UserDto }> {
    return this.http.get<{ isSuccess: boolean; result: UserDto }>(`${this.apiUrl}/${userId}`);
  }

  // The logged-in user's own profile, for every role (the user comes from the token)
  getMe(): Observable<{ isSuccess: boolean; result: UserDto }> {
    return this.http.get<{ isSuccess: boolean; result: UserDto }>('api/UserActions/me');
  }



  // Create an admin user
  createAdminUser(adminUser: any): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/create-admin`, adminUser);
  }

  // Update a user as an admin
  updateUserAsAdmin(userId: string, user: any): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/updateAsAdmin/${userId}`, user);
  }

  // Delete a user
  deleteUser(userId: string): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${userId}`);
  }
}