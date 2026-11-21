import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResponse, FriendshipItem, FriendsOverview, UserSearchResult } from '../Models/interfaces';

// The two sharing settings, as they appear in the API's URLs
export type SharingSetting = 'physical-library' | 'ebooks';

// Friends and friend requests (#84). The user comes from the token; someone else's
// or an unknown friendship id gives a 404.
@Injectable({ providedIn: 'root' })
export class FriendsService {
  private apiUrl = 'api/friends';

  constructor(private http: HttpClient) {}

  // Friends, requests sent and received, and "used of limit" for both sharing settings
  getOverview(): Observable<ApiResponse<FriendsOverview>> {
    return this.http.get<ApiResponse<FriendsOverview>>(this.apiUrl);
  }

  // User names starting with the text, without yourself, friends and open requests
  search(userName: string): Observable<ApiResponse<UserSearchResult[]>> {
    const params = new HttpParams().set('userName', userName);
    return this.http.get<ApiResponse<UserSearchResult[]>>(`${this.apiUrl}/search`, { params });
  }

  // If that user already asked me, this accepts their request: the result's status is then Accepted
  sendRequest(userName: string): Observable<ApiResponse<FriendshipItem>> {
    return this.http.post<ApiResponse<FriendshipItem>>(`${this.apiUrl}/requests`, { userName });
  }

  // Only the person asked can accept or decline
  accept(id: number): Observable<ApiResponse<FriendshipItem>> {
    return this.http.put<ApiResponse<FriendshipItem>>(`${this.apiUrl}/requests/${id}/accept`, {});
  }

  decline(id: number): Observable<ApiResponse<FriendshipItem>> {
    return this.http.put<ApiResponse<FriendshipItem>>(`${this.apiUrl}/requests/${id}/decline`, {});
  }

  // The requester takes back a pending request
  cancel(id: number): Observable<ApiResponse<boolean>> {
    return this.http.delete<ApiResponse<boolean>>(`${this.apiUrl}/requests/${id}`);
  }

  // Either friend ends the friendship; both directions stop sharing at once
  remove(id: number): Observable<ApiResponse<boolean>> {
    return this.http.delete<ApiResponse<boolean>>(`${this.apiUrl}/${id}`);
  }

  // Turns one of my own sharing settings for this friend on or off. Turning one on
  // past the limit gives a 400 whose message names the limit.
  setSharing(id: number, setting: SharingSetting, on: boolean): Observable<ApiResponse<FriendshipItem>> {
    return this.http.put<ApiResponse<FriendshipItem>>(`${this.apiUrl}/${id}/sharing/${setting}`, { on });
  }
}
