import { Injectable } from '@angular/core';
import { CanActivate, CanActivateChild, Router, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthService } from './auth.service';

@Injectable({ providedIn: 'root' })
export class AuthGuard implements CanActivate, CanActivateChild {
  constructor(private authService: AuthService, private router: Router) {}

  canActivate(_route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean {
    if (this.authService.isAuthenticated()) {
      // If trying to access the start page while authenticated, redirect to /home
      if (state.url === '/' || state.url === '') {
        this.router.navigate(['/home']);
        return false;
      }
      return true;
    } else {
      // If not authenticated and trying to access a protected route, redirect to start
      if (state.url !== '/' && state.url !== '') {
        this.router.navigate(['']);
        return false;
      }
      return true;
    }
  }

  canActivateChild(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean {
    return this.canActivate(route, state);
  }
}