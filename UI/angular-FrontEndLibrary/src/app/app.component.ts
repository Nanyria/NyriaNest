//app.component.ts

import { Component, ChangeDetectionStrategy } from '@angular/core';

import { RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from './Services/auth.service';
import { TopbarComponent } from './components/layout/topbar/topbar.component';
import { SidebarComponent } from './components/layout/sidebar/sidebar.component';
import { ToastComponent } from './components/layout/toast/toast.component';
import { ConfirmDialogComponent } from './components/layout/confirm-dialog/confirm-dialog.component';
@Component({
  selector: 'app-root',
  standalone: true,
  templateUrl: './app.component.html', // Split into a separate HTML file
  styleUrls: ['./app.component.css'], // Split into a separate CSS file
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [RouterModule, FormsModule, TopbarComponent, SidebarComponent, ToastComponent, ConfirmDialogComponent],
})
export class AppComponent {
  title = 'angular-FrontEndLibrary';
  sidebarOpen = false;

  constructor(private authService: AuthService) {}

  isAuthenticated(): boolean {
    return this.authService.isAuthenticated();
  }
}
