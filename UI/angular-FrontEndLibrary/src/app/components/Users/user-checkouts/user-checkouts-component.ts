import {
  Component,
  OnInit,
  ChangeDetectionStrategy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../Services/auth.service';
@Component({
  selector: 'app-user-checkouts',
  standalone: true,
  templateUrl: './user-checkouts-component.html',
  styleUrls: ['./user-checkouts-component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [CommonModule, FormsModule],
})
export class UserCheckoutsComponent implements OnInit {
  checkedOutBooks: any[] = [];
  constructor(private authService: AuthService) {}

  ngOnInit(): void {
    this.authService.getCurrentUser().subscribe((user) => {
      this.checkedOutBooks = user?.checkedOutBooks || [];
    });
  }
}
