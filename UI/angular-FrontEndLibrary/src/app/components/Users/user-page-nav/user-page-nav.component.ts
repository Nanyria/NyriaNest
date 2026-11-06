import {
  Component,
  OnInit,
  Input,
  ChangeDetectionStrategy,
} from '@angular/core';

import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
@Component({
  selector: 'app-user-page-nav',
  standalone: true,
  templateUrl: './user-page-nav.component.html',
  styleUrls: ['./user-page-nav.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [FormsModule, RouterModule],
})
export class UserPageNavComponent implements OnInit {
  @Input() currentUrl: string = '';
  @Input() hasNewNotification: boolean = false;
  editMode = false;

  constructor() {}

  ngOnInit(): void {}
  isActive(path: string): boolean {
    return this.currentUrl.includes(path);
  }
}
