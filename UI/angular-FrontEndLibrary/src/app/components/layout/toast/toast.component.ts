import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from '../../../Services/toast.service';

@Component({
  selector: 'app-toast',
  standalone: true,
  templateUrl: './toast.component.html',
  styleUrls: ['./toast.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ToastComponent {
  readonly toast = inject(ToastService);
}
