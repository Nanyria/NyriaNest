import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  inject,
  viewChild,
} from '@angular/core';
import { ConfirmService } from '../../../Services/confirm.service';

// A native <dialog> opened with showModal(): it blocks the page behind it,
// keeps keyboard focus inside and closes on Esc without extra code
@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  templateUrl: './confirm-dialog.component.html',
  styleUrls: ['./confirm-dialog.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ConfirmDialogComponent {
  readonly confirm = inject(ConfirmService);
  private dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  // The frame ornaments; CSS mirrors the one corner drawing into each corner
  readonly corners = ['tl', 'tr', 'bl', 'br'];
  readonly edges = ['top', 'bottom'];

  // Four-point star like the site's other ornaments: sides curve in to the centre
  starPath(x: number, y: number, r: number): string {
    return `M${x} ${y - r} Q${x} ${y} ${x + r} ${y} Q${x} ${y} ${x} ${y + r} Q${x} ${y} ${x - r} ${y} Q${x} ${y} ${x} ${y - r}Z`;
  }

  constructor() {
    // After render, so the buttons exist and autofocus lands on Cancel
    afterRenderEffect(() => {
      const open = this.confirm.current() !== null;
      const dialog = this.dialog().nativeElement;
      if (open && !dialog.open) dialog.showModal();
      if (!open && dialog.open) dialog.close();
    });
  }

  // The content fills the dialog, so a click on the dialog itself is the backdrop
  onClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.confirm.close(false);
    }
  }
}
