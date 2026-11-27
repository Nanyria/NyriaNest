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
import { DEFAULT_PROFILE_PICTURE } from '../../../Helpers/profile-pictures';

// A card's status: the words, the theme's nn-tag--… colour and a Bootstrap icon
interface CardStatus {
  text: string;
  cls: string;
  icon: string;
}

const LENT_OUT: CardStatus = { text: 'Lent out', cls: 'nn-tag--checked-out', icon: 'bi-box-arrow-up-right' };

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
  readonly defaultPicture = DEFAULT_PROFILE_PICTURE;

  isInReadList(book: LibraryBook): boolean {
    return this.user?.readList?.some((item) => +item.bookId === +book.bookId) ?? false;
  }

  // Only flag books that can't be borrowed right now; "Available" is the norm.
  // In the theme's status colours, as on Manage books: only Overdue is red.
  // Shelves and grid show the icon on the cover (the text on hover), the list a tag.
  status(book: LibraryBook): CardStatus | null {
    if (this.isFriendsBook(book)) return this.friendsStatus(book);
    // I lend my own books out rather than someone checking them out
    if (book.isMine && book.bookStatus === BookStatusEnum.CheckedOut) return LENT_OUT;
    switch (book.bookStatus) {
      case BookStatusEnum.Reserved:
        return { text: BookStatusOptions.displayNames[book.bookStatus], cls: 'nn-tag--reserved', icon: 'bi-hourglass-split' };
      case BookStatusEnum.CheckedOut:
        return { text: BookStatusOptions.displayNames[book.bookStatus], cls: 'nn-tag--checked-out', icon: 'bi-box-arrow-up-right' };
      case BookStatusEnum.Overdue:
        return { text: BookStatusOptions.displayNames[book.bookStatus], cls: 'nn-tag--overdue', icon: 'bi-exclamation-lg' };
      default:
        return null;
    }
  }

  private isFriendsBook(book: LibraryBook): boolean {
    return !book.isMine && !!book.ownerName;
  }

  // A friend's book (#85): where I stand with it. Asked and reserved are both
  // "Requested" (the popup tells them apart). Green = mine for now, amber =
  // waiting, brown = someone else has it; their overdue loan is just "Lent out".
  private friendsStatus(book: LibraryBook): CardStatus | null {
    if (book.isCheckedOutByMe) {
      return book.bookStatus === BookStatusEnum.Overdue
        ? { text: 'Overdue', cls: 'nn-tag--overdue', icon: 'bi-exclamation-lg' }
        : { text: 'You have it', cls: 'nn-tag--available', icon: 'bi-check-lg' };
    }
    if (book.myRequestId != null || book.isReservedByMe) {
      return { text: 'Requested', cls: 'nn-tag--reserved', icon: 'bi-hourglass-split' };
    }
    if (book.bookStatus === BookStatusEnum.CheckedOut || book.bookStatus === BookStatusEnum.Overdue) {
      return LENT_OUT;
    }
    return null; // free: nothing to flag
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
