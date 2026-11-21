import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { AbstractControl, FormControl, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { MyBook } from '../../../Models/interfaces';
import { MyBooksService } from '../../../Services/my-books.service';
import { dateOnly, formatDay, todayIso } from './loan-dates';

export type LoanDialogMode = 'lend' | 'return';

// "yyyy-MM-dd" strings compare correctly as text
const notAfter = (max: () => string) => (c: AbstractControl): ValidationErrors | null =>
  c.value && c.value > max() ? { tooLate: true } : null;
const notBefore = (min: () => string) => (c: AbstractControl): ValidationErrors | null =>
  c.value && c.value < min() ? { tooEarly: true } : null;

// Lend a book to someone by name, or mark it returned. Looks like the confirm
// dialog, but has fields, so it's its own small component (ConfirmService is yes/no only).
// Opened by rendering it; tells the page it closed through "closed" (true = saved).
@Component({
  selector: 'app-loan-dialog',
  standalone: true,
  templateUrl: './loan-dialog.component.html',
  styleUrls: ['../../layout/confirm-dialog/confirm-dialog.component.css', './loan-dialog.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [ReactiveFormsModule],
})
export class LoanDialogComponent {
  private myBooks = inject(MyBooksService);
  private dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  readonly book = input.required<MyBook>();
  readonly mode = input.required<LoanDialogMode>();
  readonly closed = output<boolean>();

  readonly today = todayIso();
  readonly saving = signal(false);
  readonly submitted = signal(false);
  readonly error = signal<string | null>(null); // the API's message, e.g. a 400 or 409

  readonly borrowerName = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(100), (c) => (c.value?.trim() ? null : { required: true })],
  });
  readonly lentOn = new FormControl(this.today, {
    nonNullable: true,
    validators: [Validators.required, notAfter(() => this.today)],
  });
  readonly returnedOn = new FormControl(this.today, {
    nonNullable: true,
    validators: [Validators.required, notAfter(() => this.today), notBefore(() => this.lentOnDay())],
  });

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.showModal());
  }

  /** The day it was lent, the earliest it can have come back */
  lentOnDay(): string {
    const lentOn = this.book().lentOn;
    return lentOn ? dateOnly(lentOn) : '';
  }

  lentOnText(): string {
    return formatDay(this.book().lentOn);
  }

  save(): void {
    this.submitted.set(true);
    this.error.set(null);
    const b = this.book();
    let request: Observable<unknown>;
    if (this.mode() === 'lend') {
      if (this.borrowerName.invalid || this.lentOn.invalid) return;
      request = this.myBooks.lend(b.bookId, this.borrowerName.value.trim(), this.lentOn.value);
    } else {
      if (this.returnedOn.invalid) return;
      request = this.myBooks.markReturned(b.bookId, this.returnedOn.value);
    }

    this.saving.set(true);
    request.subscribe({
      next: () => this.close(true),
      error: (err) => {
        this.saving.set(false);
        this.error.set(
          err?.error?.errorMessages?.[0] ??
            (this.mode() === 'lend' ? "Couldn't lend the book. Try again." : "Couldn't mark the book as returned. Try again."),
        );
      },
    });
  }

  // Tells the page once, however the dialog was closed (button, Esc, backdrop)
  private finished = false;

  close(saved: boolean): void {
    if (this.finished) return;
    this.finished = true;
    const dialog = this.dialog().nativeElement;
    if (dialog.open) dialog.close();
    this.closed.emit(saved);
  }

  // Esc closes the native dialog by itself, before any button is pressed
  onNativeClose(): void {
    this.close(false);
  }

  // The content fills the dialog, so a click on the dialog itself is the backdrop
  onClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement && !this.saving()) this.close(false);
  }
}
