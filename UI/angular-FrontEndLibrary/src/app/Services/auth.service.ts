import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap } from 'rxjs/operators';
import { ILoggedInUser } from '../Models/interfaces';
import { UserService } from './user.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { Router } from '@angular/router';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private apiUrl = 'api/Auth';
  private tokenKey = 'auth_token';
  private authStatus = new BehaviorSubject<boolean>(this.hasToken());
  private currentUserSubject = new BehaviorSubject<ILoggedInUser | null>(null);

constructor(private http: HttpClient, private userService: UserService, private router: Router) {
  const userId = this.getUserId();
  const storedUser = localStorage.getItem('current_user');
  if (userId && storedUser) {
    this.currentUserSubject.next(JSON.parse(storedUser));
    // Optionally, refresh from backend:
    this.userService.getMe().pipe(
      catchError(err => {
        if (err.status === 401) {
          this.logout();
          this.router.navigate(['']);
        }
        return of(null);
      })
    ).subscribe(response => {
      if (response && response.isSuccess) {
        const user = this.mapUserResponse(response.result);
        this.currentUserSubject.next(user);
        localStorage.setItem('current_user', JSON.stringify(user));
      }
    });
  } else if (userId) {
    // Fallback: fetch from backend if not in localStorage
    this.userService.getMe().pipe(
      catchError(err => {
        if (err.status === 401) {
          this.logout();
          this.router.navigate(['']);
        }
        return of(null);
      })
    ).subscribe(response => {
      if (response && response.isSuccess) {
        const user = this.mapUserResponse(response.result);
        this.currentUserSubject.next(user);
        localStorage.setItem('current_user', JSON.stringify(user));
      }
    });
  }
}

  login(username: string, password: string): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/login`, { username, password }).pipe(
      tap(response => {
        if (response && response.token) {
          localStorage.setItem(this.tokenKey, response.token);
          this.authStatus.next(true);
          const userId = this.getUserId();
          if (userId) {
            this.userService.getMe().subscribe(res => {
              if (res.isSuccess) {
                this.currentUserSubject.next(this.mapUserResponse(res.result));
              }
            });
          }
        }
      })
    );
  }
  refreshCurrentUser(): void {
    const userId = this.getUserId();
    if (userId) {
      this.userService.getMe().subscribe(response => {
        if (response.isSuccess) {
          this.currentUserSubject.next(this.mapUserResponse(response.result));
        }
      });
    }
  }

  setCurrentUser(user: ILoggedInUser) {
    this.currentUserSubject.next(user);
    localStorage.setItem('current_user', JSON.stringify(user));
  }

  logout(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem('current_user');
    this.authStatus.next(false);
    this.currentUserSubject.next(null);
  }
  
  private mapUserResponse(result: any): ILoggedInUser {
    return {
      ...result,
      userId: result.userId ?? '',
      adminRole: this.isAdminFromToken(),
      isSuperAdmin: this.isSuperAdminFromToken()
    };
  }
  isAuthenticated(): boolean {
    return this.hasToken();
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  getAuthStatus(): Observable<boolean> {
    return this.authStatus.asObservable();
  }

  getCurrentUser(): Observable<ILoggedInUser | null> {
    return this.currentUserSubject.asObservable();
  }

  getUserId(): string | null {
    const token = this.getToken();
    if (!token) return null;
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload.userId || payload.jti || null; // Prefer userId, fallback to jti
    } catch {
      return null;
    }
  }

  private getRolesFromToken(): string[] {
    const token = this.getToken();
    if (!token) return [];
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      let roles = payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"];
      if (!roles) return [];
      if (Array.isArray(roles)) return roles;
      return [roles];
    } catch {
      return [];
    }
  }

  isAdminFromToken(): boolean {
    const roles = this.getRolesFromToken();
    return roles.includes('Admin') || roles.includes('SuperAdmin');
  }

  isSuperAdminFromToken(): boolean {
    const roles = this.getRolesFromToken();
    return roles.includes('SuperAdmin');
  }

  private hasToken(): boolean {
    return !!localStorage.getItem(this.tokenKey);
  }
}