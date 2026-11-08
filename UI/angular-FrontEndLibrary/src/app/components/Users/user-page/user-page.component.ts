import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterOutlet } from '@angular/router';

// Wrapper for the user's own pages (settings, reading list, reservations, loans).
// Navigation lives in the topbar's My books menu and Settings link (and the
// bottom bar on small screens), so this only frames the content.
@Component({
  selector: 'app-user-page',
  standalone: true,
  templateUrl: './user-page.component.html',
  styleUrls: ['./user-page.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [RouterOutlet],
})
export class UserPageComponent {}
