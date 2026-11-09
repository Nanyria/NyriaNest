import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

// Shown on the login card after the app logs someone out (read by LoginComponent)
export const SESSION_ENDED_KEY = 'session_ended';

// Set while the page reloads after a session ended. Other code (AuthService's own 401
// handling) may show the login card before the reload happens; that card must leave
// the notice for the reloaded page. Resets with the reload.
export const sessionEnd = { reloading: false };

// Adds the login token to every request. If the API refuses a token the app sent
// (it expired, or the user's password or email was changed elsewhere, #113), the
// login is cleared and the app reloads on the start page, which says why.
// A full reload, so nothing of the old user is left in memory.
export const AuthInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('auth_token');
  if (!token) {
    return next(req);
  }

  const cloned = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  return next(cloned).pipe(
    catchError((err: HttpErrorResponse) => {
      // Only when this token was refused: a newer one may already have replaced it
      if (err.status === 401 && localStorage.getItem('auth_token') === token) {
        const reason = err.headers?.get('X-Session-Ended') === 'credentials-changed' ? 'credentials-changed' : 'expired';
        localStorage.removeItem('auth_token');
        localStorage.removeItem('current_user');
        sessionStorage.setItem(SESSION_ENDED_KEY, reason);
        sessionEnd.reloading = true;
        window.location.assign('/');
      }
      return throwError(() => err);
    }),
  );
};
