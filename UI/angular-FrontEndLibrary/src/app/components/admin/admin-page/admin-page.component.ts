import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterOutlet } from '@angular/router';

// Wrapper for the admin pages. Navigation lives in the topbar's Admin dropdown
// (and the sidebar drawer on small screens), so this only frames the content.
@Component({
  selector: 'app-admin-page',
  standalone: true,
  templateUrl: './admin-page.component.html',
  styleUrls: ['./admin-page.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [RouterOutlet],
})
export class AdminPageComponent {}
