import {
  Component,
  Input,
  Output,
  EventEmitter,
  ChangeDetectionStrategy,
} from '@angular/core';

import { LibraryBook, ILoggedInUser } from '../../../Models/interfaces';
import { BookStatusEnum } from '../../../Helpers/Enums/enum';
import { BookStatusOptions, GenreOptions } from '../../../Helpers/Helper';

@Component({
  selector: 'app-book-cover-list',
  templateUrl: './book-cover-list.component.html',
  styleUrls: ['./book-cover-list.component.css'],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class BookCoverListComponent {
  @Input() books: LibraryBook[] = [];
  @Input() currentUserId!: string;
  @Input() user: ILoggedInUser | null = null;
  @Input() viewMode: 'grid' | 'list' = 'grid';
  @Input() shelf = false;
  @Output() bookRowClicked = new EventEmitter<LibraryBook>();
  @Output() readListToggled = new EventEmitter<LibraryBook>();

  genreOptions = GenreOptions;
  readonly fallbackCover = 'assets/images/book-covers/BrownBook.svg';

  isInReadList(book: LibraryBook): boolean {
    return this.user?.readList?.some((item) => +item.bookId === +book.bookId) ?? false;
  }

  // Only flag books that can't be borrowed right now; "Available" is the norm
  statusBadge(book: LibraryBook): string | null {
    switch (book.bookStatus) {
      case BookStatusEnum.Reserved:
      case BookStatusEnum.CheckedOut:
      case BookStatusEnum.Overdue:
        return BookStatusOptions.displayNames[book.bookStatus];
      default:
        return null;
    }
  }

  // The theme's status colours, as on Manage books: only Overdue is red
  statusClass(book: LibraryBook): string {
    switch (book.bookStatus) {
      case BookStatusEnum.Reserved: return 'nn-tag--reserved';
      case BookStatusEnum.Overdue: return 'nn-tag--overdue';
      default: return 'nn-tag--checked-out';
    }
  }


  openBook(book: LibraryBook, event?: Event) {
    if (event instanceof KeyboardEvent) {
      event.preventDefault();
    }
    this.bookRowClicked.emit(book);
  }

  toggleReadList(book: LibraryBook, event: Event) {
    event.stopPropagation(); // don't also open the book
    this.readListToggled.emit(book);
  }
}
