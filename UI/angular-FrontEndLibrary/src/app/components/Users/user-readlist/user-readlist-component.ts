import {
  Component,
  OnInit,
  Input,
  ChangeDetectionStrategy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../Services/auth.service';
@Component({
  selector: 'app-user-readlist',
  standalone: true,
  templateUrl: './user-readlist-component.html',
  styleUrls: ['./user-readlist-component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [CommonModule, FormsModule],
})
export class UserReadlistComponent implements OnInit {
  @Input() readList: any[] = [];
  editMode = false;

  constructor(private authService: AuthService) {}

  ngOnInit(): void {
    this.authService.getCurrentUser().subscribe((user) => {
      this.readList = user?.readList || [];
      console.log('Read List:', this.readList);
    });
  }
}
