import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  computed,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { BookReview, LibraryBook } from '../../../Models/interfaces';
import { BookTypeEnums, LanguageDisplayNames, LanguageEnums } from '../../../Helpers/Enums/enum';
import { BookTypeOptions, GenreOptions } from '../../../Helpers/Helper';
import { formatMoment } from '../../../Helpers/loan-dates';

/** An icon button under the cover; the label is its hover text and accessible name */
export interface BookAction {
  label: string;
  icon: string; // Bootstrap icon, e.g. 'bi-bookmark'
  action: () => void;
  disabled?: boolean;
  pressed?: boolean; // for toggles, e.g. in the reading list
}

/** Can I get this book? In words, e.g. "On your shelf" or "No file yet" */
export interface BookStatusLine {
  text: string;
  detail?: string;
  tone: 'ok' | 'out' | 'muted';
}

const REVIEWS_SHOWN = 3;

// The Library's book popup. Shows what the page hands it; the page decides the
// actions and status by format and owner. A native <dialog> with showModal():
// focus stays inside and Esc closes it without extra code.
@Component({
  selector: 'app-book-details-dialog',
  standalone: true,
  templateUrl: './book-details-dialog.component.html',
  styleUrls: ['./book-details-dialog.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookDetailsDialogComponent {
  /** The book to show; null closes the dialog */
  readonly book = input<LibraryBook | null>(null);
  readonly actions = input<BookAction[]>([]);
  readonly status = input<BookStatusLine | null>(null);
  /** null while loading */
  readonly reviews = input<BookReview[] | null>(null);
  readonly closed = output<void>();

  private dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  readonly allReviewsShown = signal(false);
  readonly fallbackCover = 'assets/images/book-covers/BrownBook.svg';

  readonly meta = computed(() => {
    const b = this.book();
    if (!b) return [];
    return [
      GenreOptions.displayNames[b.genre],
      BookTypeOptions.displayNames[b.bookType],
      b.publicationYear ? String(b.publicationYear) : '',
      b.language !== LanguageEnums.Undefined ? LanguageDisplayNames[b.language] : '',
    ].filter(Boolean);
  });

  readonly formatIcon = computed(() => {
    switch (this.book()?.bookType) {
      case BookTypeEnums.EBook: return 'bi-tablet';
      case BookTypeEnums.Audiobook: return 'bi-headphones';
      default: return 'bi-book';
    }
  });

  readonly shownReviews = computed(() => {
    const all = this.reviews() ?? [];
    return this.allReviewsShown() ? all : all.slice(0, REVIEWS_SHOWN);
  });

  readonly hiddenReviewCount = computed(
    () => (this.reviews()?.length ?? 0) - this.shownReviews().length,
  );

  constructor() {
    afterRenderEffect(() => {
      const open = this.book() !== null;
      const dialog = this.dialog().nativeElement;
      if (open && !dialog.open) {
        this.allReviewsShown.set(false);
        dialog.showModal();
      }
      if (!open && dialog.open) dialog.close();
    });
  }

  // The content fills the dialog, so a click on the dialog itself is the backdrop
  onClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) this.dialog().nativeElement.close();
  }

  // Esc, the × and the backdrop all end up here through the dialog's close event
  onClose(): void {
    if (this.book() !== null) this.closed.emit();
  }

  close(): void {
    this.dialog().nativeElement.close();
  }

  // Ratings are whole stars, 1–5
  stars(rating: number | undefined): number[] {
    const n = Math.max(0, Math.min(5, Math.round(rating ?? 0)));
    return Array.from({ length: 5 }, (_, i) => (i < n ? 1 : 0));
  }

  reviewDate(review: BookReview): string {
    return formatMoment(review.createdAt);
  }
}
