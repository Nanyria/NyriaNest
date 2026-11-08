import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterOutlet } from '@angular/router';

// Wrapper for the user's own pages (reading list, loans, reservations).
// Navigation lives in the topbar's My books menu (and the bottom bar on small
// screens), so this only frames the content.
@Component({
  selector: 'app-user-page',
  standalone: true,
  templateUrl: './user-page.component.html',
  styleUrls: ['./user-page.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [RouterOutlet],
})
export class UserPageComponent {}
