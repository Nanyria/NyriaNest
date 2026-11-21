import {
  AfterViewInit,
  Component,
  DestroyRef,
  HostListener,
  ViewChild,
  ChangeDetectionStrategy,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';

import { LibraryBook, ILoggedInUser } from '../../../Models/interfaces';
import { LibraryService } from '../../../Services/library.services';
import { BookService } from '../../../Services/book.services';
import { UserActionsService } from '../../../Services/user-actions.service';
import { AuthService } from '../../../Services/auth.service';
import { LibraryUiService } from '../../../Services/library-ui.service';
import { ToastService } from '../../../Services/toast.service';
import { ConfirmService } from '../../../Services/confirm.service';
import { Observable } from 'rxjs';
import { BookStatusEnum, BookTypeEnums, GenreEnums } from '../../../Helpers/Enums/enum';
import { BookCoverListComponent } from '../book-cover-list/book-cover-list.component';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  BookAction,
  BookDetailsDialogComponent,
  BookStatusLine,
} from '../book-details-dialog/book-details-dialog.component';
import { BookReview, MyBook } from '../../../Models/interfaces';
import { MyBooksService } from '../../../Services/my-books.service';
import { formatDay } from '../../../Helpers/loan-dates';
import { ScrollStripComponent } from '../../layout/scroll-strip/scroll-strip.component';
import {
  BookTypeOptions,
  GenreOptions,
} from '../../../Helpers/Helper';
import {
  GENRE_CARD_STYLES,
  GenreCardStyle,
} from '../../../Helpers/genre-styles/genre-cards';

type StatusFilter = 'all' | 'available' | 'reserved' | 'checkedOut';
type LibraryFilter = 'all' | 'mine' | 'shared';
type SortOption = 'title' | 'author' | 'added' | 'newest' | 'popular';
type BookFormatFilter = 'physical' | 'ebook' | 'audiobook' | 'all';

// Genre chips shown in the filter panel before "+ More" is pressed
const FILTER_GENRES_SHOWN = 6;

const byRecentlyAdded = (a: LibraryBook, b: LibraryBook) =>
  (Date.parse(b.dateAdded) || 0) - (Date.parse(a.dateAdded) || 0);
const byNewest = (a: LibraryBook, b: LibraryBook) =>
  (Number(b.publicationYear) || 0) - (Number(a.publicationYear) || 0);
const byMostReviewed = (a: LibraryBook, b: LibraryBook) => b.reviewCount - a.reviewCount;

@Component({
  selector: 'app-library',
  templateUrl: './library-page.component.html',
  styleUrls: ['./library-page.component.css'],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [
    BookCoverListComponent,
    BookDetailsDialogComponent,
    ScrollStripComponent,
    FormsModule,
    NgTemplateOutlet,
    RouterLink,
  ],
})
export class LibraryComponent implements AfterViewInit {
  private destroyRef = inject(DestroyRef);
  user: ILoggedInUser | null = null;
  currentUserId: string = '';
  books: LibraryBook[] = []; // Every book; the template shows visibleBooks
  loading = true;
  loadError = false;

  // Filtering and sorting happen client-side over the loaded books
  searchTerm = '';
  selectedGenre: GenreEnums = GenreEnums.All;
  selectedStatus: StatusFilter = 'all';
  selectedLibrary: LibraryFilter = 'all';
  selectedBookFormat: BookFormatFilter = 'all';
  selectedMinimumRating = 0;
  sortBy: SortOption = 'title';
  viewMode: 'grid' | 'list' = 'grid';
  showAll = false; // "See all" pressed: list every book even without filters
  genresExpanded = false;

  // The filter panel edits a draft that only takes effect on "Apply filters"
  filtersOpen = false;
  allFilterGenresShown = false;
  draft = {
    genre: GenreEnums.All,
    status: 'all' as StatusFilter,
    library: 'all' as LibraryFilter,
    format: 'all' as BookFormatFilter,
    minimumRating: 0,
  };

  // Shared = not mine for now; friends' books get their own choice in #85
  libraryFilters: { value: LibraryFilter; label: string }[] = [
    { value: 'all', label: 'All libraries' },
    { value: 'mine', label: 'Mine' },
    { value: 'shared', label: 'Shared library' },
  ];
  statusFilters: { value: StatusFilter; label: string }[] = [
    { value: 'all', label: 'All' },
    { value: 'available', label: 'Available' },
    { value: 'reserved', label: 'Reserved' },
    { value: 'checkedOut', label: 'Checked out' },
  ];
  formatFilters: { value: BookFormatFilter; label: string }[] = [
    { value: 'physical', label: 'Physical book' },
    { value: 'ebook', label: 'E-book' },
    { value: 'audiobook', label: 'Audiobook' },
  ];
  ratingFilters = [0, 3, 4, 5];
  sortOptions: { value: SortOption; label: string }[] = [
    { value: 'title', label: 'Title (A–Z)' },
    { value: 'author', label: 'Author (A–Z)' },
    { value: 'added', label: 'Recently added' },
    { value: 'newest', label: 'Newest published' },
    { value: 'popular', label: 'Most reviewed' },
  ];
  // One illustrated card per genre ("All" is the header link, not a card)
  genreCards = Object.values(GenreEnums)
    .filter((g): g is GenreEnums => typeof g === 'number')
    .filter((g) => g !== GenreEnums.None && g !== GenreEnums.All)
    .map((g) => ({
      value: g,
      label: GenreOptions.displayNames[g],
      ...(GENRE_CARD_STYLES[g] ??
        ({ tint: '#ecdcc4', ink: '#6b4a2a', icon: 'bi-bookmark' } as GenreCardStyle)),
    }));
  readonly allGenres = GenreEnums.All;

  @ViewChild('genreStrip') genreStrip?: ScrollStripComponent;
  // The book popup is open while a book is selected
  selectedBook: LibraryBook | null = null;
  selectedBookReviews: BookReview[] | null = null; // null while loading
  ownBookLoan: MyBook | null = null; // my own selected book, with its loan
  bookActions: BookAction[] = [];
  isSuperAdmin: boolean = false;
  bookTypeOptions = BookTypeOptions;
  genreOptions = GenreOptions;
  constructor(
    private libraryService: LibraryService,
    private userActionsService: UserActionsService,
    private authService: AuthService,
    private libraryUi: LibraryUiService,
    private router: Router,
    private route: ActivatedRoute,
    private toast: ToastService,
    private confirmDialog: ConfirmService,
    private bookService: BookService,
    private myBooksService: MyBooksService,
  ) {}

  ngOnInit(): void {
    // The panel starts closed; tell the topbar, and again when leaving the page
    this.libraryUi.filtersOpen.set(false);
    this.destroyRef.onDestroy(() => this.libraryUi.filtersOpen.set(false));
    this.libraryUi.filtersToggled$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => (this.filtersOpen ? this.closeFilters() : this.openFilters()));
    this.authService
      .getCurrentUser()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((user) => {
        this.user = user;
        this.currentUserId = user?.userId ?? '';
        this.isSuperAdmin = !!user?.isSuperAdmin; // <-- Set here, after user is loaded
        if (this.selectedBook) {
          this.updateBookActions(this.selectedBook);
        }
      });
    // Genre, search and "see all" live in the URL (/library?genre=Fantasy&q=dune)
    // so the topbar search and links can point straight at a filtered view
    this.route.queryParamMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
        const genre = GenreEnums[params.get('genre') as keyof typeof GenreEnums];
        this.selectedGenre = genre ?? GenreEnums.All;
        this.searchTerm = params.get('q') ?? '';
        this.showAll = params.get('view') === 'all';
        // The topbar's Filters button on other pages links here with
        // filters=open; open the panel and drop the flag from the URL
        if (params.get('filters') === 'open') {
          this.openFilters();
          this.router.navigate([], {
            relativeTo: this.route,
            queryParams: { filters: null },
            queryParamsHandling: 'merge',
            replaceUrl: true,
          });
        }
      });
    this.getAllBooks();
  }

  getAllBooks() {
    this.libraryService.getAllBooks().subscribe(
      (response: { isSuccess: boolean; result: LibraryBook[] }) => {
        this.books =
          response.isSuccess && Array.isArray(response.result)
            ? response.result
            : [];
        this.loading = false;
        this.loadError = false;
        // The "…ByMe" flags come from the server, so an open popup takes the fresh copy
        if (this.selectedBook) {
          const fresh = this.books.find((b) => b.bookId === this.selectedBook!.bookId);
          if (fresh) {
            this.selectedBook = fresh;
            this.updateBookActions(fresh);
          }
        }
      },
      () => {
        this.books = [];
        this.loading = false;
        this.loadError = true;
      },
    );
  }

  get visibleBooks(): LibraryBook[] {
    const term = this.searchTerm.trim().toLowerCase();
    const filtered = this.books.filter(
      (book) =>
        (this.selectedGenre === GenreEnums.All ||
          book.genre === this.selectedGenre) &&
        this.matchesStatus(book) &&
        this.matchesLibrary(book) &&
        this.matchesFormat(book) &&
        this.matchesMinimumRating(book) &&
        (!term ||
          book.title?.toLowerCase().includes(term) ||
          book.author?.toLowerCase().includes(term) ||
          GenreOptions.displayNames[book.genre]?.toLowerCase().includes(term)),
    );
    return filtered.sort((a, b) => {
      switch (this.sortBy) {
        case 'author':
          return (a.author ?? '').localeCompare(b.author ?? '');
        case 'added':
          return byRecentlyAdded(a, b);
        case 'newest':
          return byNewest(a, b);
        case 'popular':
          return byMostReviewed(a, b);
        default:
          return (a.title ?? '').localeCompare(b.title ?? '');
      }
    });
  }

  private matchesFormat(book: LibraryBook): boolean {
    switch (this.selectedBookFormat) {
      case 'physical':
        return (
          book.bookType === BookTypeEnums.Paperback ||
          book.bookType === BookTypeEnums.Hardcover
        );
      case 'ebook':
        return book.bookType === BookTypeEnums.EBook;
      case 'audiobook':
        return book.bookType === BookTypeEnums.Audiobook;
      default:
        return true;
    }
  }

  private matchesMinimumRating(book: LibraryBook): boolean {
    return (
      !this.selectedMinimumRating ||
      (book.averageRating ?? 0) >= this.selectedMinimumRating
    );
  }

  // ---------- Browse view: shelves of books ----------
  get newArrivalBooks(): LibraryBook[] {
    return [...this.books].sort(byRecentlyAdded).slice(0, 12);
  }

  get popularBooks(): LibraryBook[] {
    return [...this.books].sort(byMostReviewed).slice(0, 12);
  }

  // Any search, filter or "see all" swaps the shelves for the full results list
  get isResultsMode(): boolean {
    return this.showAll || this.activeFilters.length > 0;
  }

  // The filters in effect, as removable chips above the results
  get activeFilters(): { label: string; clear: () => void }[] {
    const chips: { label: string; clear: () => void }[] = [];
    if (this.searchTerm.trim()) {
      chips.push({
        label: `"${this.searchTerm.trim()}"`,
        clear: () => {
          this.searchTerm = '';
          this.updateQueryParams();
        },
      });
    }
    if (this.selectedGenre !== GenreEnums.All) {
      chips.push({
        label: GenreOptions.displayNames[this.selectedGenre],
        clear: () => this.onGenreSelected(GenreEnums.All),
      });
    }
    if (this.selectedBookFormat !== 'all') {
      chips.push({
        label: this.formatFilters.find((f) => f.value === this.selectedBookFormat)!.label,
        clear: () => (this.selectedBookFormat = 'all'),
      });
    }
    if (this.selectedMinimumRating) {
      chips.push({
        label: `${this.selectedMinimumRating}+ stars`,
        clear: () => (this.selectedMinimumRating = 0),
      });
    }
    if (this.selectedLibrary !== 'all') {
      chips.push({
        label: this.libraryFilters.find((f) => f.value === this.selectedLibrary)!.label,
        clear: () => (this.selectedLibrary = 'all'),
      });
    }
    if (this.selectedStatus !== 'all') {
      chips.push({
        label: this.statusFilters.find((f) => f.value === this.selectedStatus)!.label,
        clear: () => (this.selectedStatus = 'all'),
      });
    }
    return chips;
  }

  get resultsTitle(): string {
    if (this.activeFilters.length === 0) return 'All books';
    return this.searchTerm.trim() ? 'Search results' : 'Filtered books';
  }

  // "See all" on a shelf lists every book in that shelf's order
  seeAll(sort: SortOption) {
    this.sortBy = sort;
    this.showAll = true;
    this.updateQueryParams();
    window.scrollTo({ top: 0 });
  }

  // ---------- Filter panel ----------
  get filterGenreChips() {
    if (this.allFilterGenresShown) return this.genreCards;
    // Keep the chosen genre visible even when it's past the first few
    return this.genreCards.filter(
      (genre, index) =>
        index < FILTER_GENRES_SHOWN || genre.value === this.draft.genre,
    );
  }

  get hiddenFilterGenreCount(): number {
    return this.genreCards.length - this.filterGenreChips.length;
  }

  openFilters() {
    this.draft = {
      genre: this.selectedGenre,
      status: this.selectedStatus,
      library: this.selectedLibrary,
      format: this.selectedBookFormat,
      minimumRating: this.selectedMinimumRating,
    };
    this.filtersOpen = true;
    this.libraryUi.filtersOpen.set(true);
  }

  closeFilters() {
    this.filtersOpen = false;
    this.libraryUi.filtersOpen.set(false);
    this.allFilterGenresShown = false;
  }

  resetDraftFilters() {
    this.draft = {
      genre: GenreEnums.All,
      status: 'all',
      library: 'all',
      format: 'all',
      minimumRating: 0,
    };
  }

  applyFilters() {
    this.selectedGenre = this.draft.genre;
    this.selectedStatus = this.draft.status;
    this.selectedLibrary = this.draft.library;
    this.selectedBookFormat = this.draft.format;
    this.selectedMinimumRating = this.draft.minimumRating;
    this.updateQueryParams();
    this.closeFilters();
  }

  @HostListener('document:keydown.escape')
  onEscape() {
    if (this.filtersOpen) {
      this.closeFilters();
    }
  }

  private matchesLibrary(book: LibraryBook): boolean {
    switch (this.selectedLibrary) {
      case 'mine':
        return book.isMine;
      case 'shared':
        return !book.isMine;
      default:
        return true;
    }
  }

  private matchesStatus(book: LibraryBook): boolean {
    switch (this.selectedStatus) {
      case 'available':
        return (
          book.bookStatus === BookStatusEnum.Available ||
          book.bookStatus === BookStatusEnum.Returned
        );
      case 'reserved':
        return book.bookStatus === BookStatusEnum.Reserved;
      case 'checkedOut':
        return (
          book.bookStatus === BookStatusEnum.CheckedOut ||
          book.bookStatus === BookStatusEnum.Overdue
        );
      default:
        return true;
    }
  }

  // Keep genre and search in the URL without adding history entries per keystroke
  updateQueryParams() {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        genre:
          this.selectedGenre === GenreEnums.All
            ? null
            : GenreEnums[this.selectedGenre],
        q: this.searchTerm.trim() || null,
        view: this.showAll ? 'all' : null,
      },
      replaceUrl: true,
    });
  }

  // Back to the browse view with no filters at all
  clearFilters() {
    this.showAll = false;
    this.sortBy = 'title';
    this.searchTerm = '';
    this.selectedGenre = GenreEnums.All;
    this.selectedStatus = 'all';
    this.selectedLibrary = 'all';
    this.selectedBookFormat = 'all';
    this.selectedMinimumRating = 0;
    this.updateQueryParams();
  }

  // The list carries most of what the popup shows; reviews are fetched, and for
  // my own physical book who it's lent to
  onBookRowClicked(book: LibraryBook) {
    this.selectedBook = book;
    this.updateBookActions(book);
    this.selectedBookReviews = null;
    this.ownBookLoan = null;
    const bookId = book.bookId;
    if (book.isMine && book.bookType !== BookTypeEnums.EBook) {
      this.myBooksService.get(+bookId).subscribe({
        // Ignore a late answer for a book that's no longer open; on failure the
        // status line keeps what the list says
        next: (response) => {
          if (this.selectedBook?.bookId === bookId && response.isSuccess) {
            this.ownBookLoan = response.result;
          }
        },
        error: () => {},
      });
    }
    this.libraryService.getBookReviews(bookId).subscribe({
      // Ignore a late answer for a book that's no longer open
      next: (response: { result?: BookReview[] }) => {
        if (this.selectedBook?.bookId === bookId) this.selectedBookReviews = response.result ?? [];
      },
      error: () => {
        if (this.selectedBook?.bookId === bookId) this.selectedBookReviews = [];
      },
    });
  }

  updateBookActions(book: LibraryBook) {
    const isInReadList =
      this.user?.readList?.some((item) => +item.bookId === +book.bookId) ??
      false;
    const readList: BookAction = {
      label: isInReadList ? 'Remove from reading list' : 'Add to reading list',
      // The same bookmark as the ribbon on the covers
      icon: isInReadList ? 'bi-bookmark-fill' : 'bi-bookmark',
      pressed: isInReadList,
      action: () => this.toggleReadList(book),
    };

    // By format and owner. Physical books that aren't mine (friends', #85) get
    // Borrow/Request, Return and Reserve there; the old borrow/reserve endpoints
    // refuse owned books, so none of them now.
    const download: BookAction[] =
      book.bookType === BookTypeEnums.EBook && book.hasEpub && this.user
        ? [{ label: 'Download', icon: 'bi-download', action: () => this.downloadEpub(book) }]
        : [];
    if (book.isMine) {
      this.bookActions = [
        ...download, // once owners have e-books
        {
          label: 'Manage in My owned books',
          icon: 'bi-bookshelf',
          action: () => this.manageOwnBook(book),
        },
        { label: 'Edit', icon: 'bi-pencil', action: () => this.editOwnBook(book) },
        readList,
      ];
    } else if (book.bookType === BookTypeEnums.EBook) {
      this.bookActions = [
        ...download,
        // Placeholder until e-books can be read online (e-book epic)
        { label: 'Read online · coming soon', icon: 'bi-book-half', action: () => {}, disabled: true },
        readList,
      ];
    } else {
      this.bookActions = [readList];
    }
  }

  // Lending, history and editing live on My owned books; its search finds the book
  manageOwnBook(book: LibraryBook) {
    this.closeBook();
    this.router.navigate(['/my/owned-books'], { queryParams: { q: book.title } });
  }

  // Fetch the file, then hand it to the browser as a download named after the book
  downloadEpub(book: LibraryBook) {
    this.bookService.downloadEpub(book.bookId).subscribe({
      next: (file) => {
        const url = URL.createObjectURL(file);
        const link = document.createElement('a');
        link.href = url;
        link.download = `${book.title.replace(/[\\/:*?"<>|]+/g, '').trim() || 'book'}.epub`;
        link.click();
        // Give the browser a moment to start the download before freeing the file
        setTimeout(() => URL.revokeObjectURL(url), 1000);
      },
      error: (err) => {
        this.toast.error(
          err?.status === 404
            ? `The file for "${book.title}" is missing.`
            : err?.status === 401
              ? 'Log in to download e-books.'
              : `Couldn't download "${book.title}".`,
        );
      },
    });
  }

  editOwnBook(book: LibraryBook) {
    this.closeBook();
    this.router.navigate(['/my/owned-books', book.bookId, 'edit']);
  }

  // Can I get it? By format first: e-books are downloaded, physical books lent.
  // Only my own physical books are in the Library for now; friends' come in #85.
  get bookStatusLine(): BookStatusLine | null {
    const book = this.selectedBook;
    if (!book) return null;
    if (book.bookType === BookTypeEnums.EBook) {
      // The Download button says enough; only say why it's missing
      if (!book.hasEpub) return { text: 'No file yet', tone: 'muted' };
      return this.user ? null : { text: 'Log in to download', tone: 'muted' };
    }
    const overdue = book.bookStatus === BookStatusEnum.Overdue;
    const lent = book.bookStatus === BookStatusEnum.CheckedOut || overdue;
    if (book.isMine) {
      // Who has it comes from api/my/books; until it's in (or if it fails), the list's status
      const loan = this.ownBookLoan?.bookId === +book.bookId ? this.ownBookLoan : null;
      if (loan) {
        if (!loan.lentTo) return { text: 'On your shelf', tone: 'ok' };
        return {
          text: `Lent to ${loan.lentTo}`,
          detail: `since ${formatDay(loan.lentOn)} · ${overdue ? 'was due' : 'due'} ${formatDay(loan.dueDate)}`,
          tone: 'out',
        };
      }
      return lent
        ? { text: 'Lent out', tone: 'out' }
        : { text: 'On your shelf', tone: 'ok' };
    }
    return lent ? { text: 'Checked out', tone: 'out' } : { text: 'Available', tone: 'ok' };
  }
  isBorrowDisabled(book: LibraryBook): boolean {
    const isCheckedOutBySomeoneElse =
      book.bookStatus === BookStatusEnum.CheckedOut && !book.isCheckedOutByMe;

    // If the book is not checked out, but there are prior reservations (not by this user)
    const hasPriorReservations =
      book.bookStatus !== BookStatusEnum.CheckedOut &&
      book.reservationCount > 0 &&
      !book.isReservedByMe;

    return isCheckedOutBySomeoneElse || hasPriorReservations;
  }

  // Not offered in the popup until #85 (friend loans); kept until then.
  // After borrowing, returning or (un)reserving, reload the list: the server
  // works out the new "…ByMe" flags and getAllBooks refreshes the open popup.
  // Returning asks first; borrowing is one click, like on Manage books.
  async toggleBorrow(book: LibraryBook) {
    if (book.isCheckedOutByMe) {
      const confirmed = await this.confirmDialog.confirm({
        title: `Return "${book.title}"?`,
        message: 'Someone else may borrow or reserve it once it is back.',
        confirmLabel: 'Return book',
        cancelLabel: 'Keep it',
        icon: 'arrow-return-left',
      });
      if (!confirmed) return;
      this.runAction(
        this.userActionsService.returnBook(book.bookId),
        { message: `You returned "${book.title}".`, icon: 'arrow-return-left' },
        `Couldn't return "${book.title}".`,
      );
    } else {
      this.runAction(
        this.userActionsService.borrowBook(book.bookId),
        { message: `You borrowed "${book.title}".`, icon: 'book' },
        `Couldn't borrow "${book.title}".`,
      );
    }
  }

  // Cancelling gives up the place in the queue, so it asks first
  async toggleReservation(book: LibraryBook) {
    if (book.isReservedByMe) {
      const confirmed = await this.confirmDialog.confirm({
        title: 'Cancel your reservation?',
        message: `You'll lose your place in the queue for "${book.title}".`,
        confirmLabel: 'Cancel reservation',
        cancelLabel: 'Keep reservation',
        danger: true,
        icon: 'calendar-x',
      });
      if (!confirmed) return;
      this.runAction(
        this.userActionsService.unreserveBook(book.bookId),
        { message: `Your reservation for "${book.title}" was cancelled.`, icon: 'calendar-x' },
        `Couldn't cancel the reservation for "${book.title}".`,
      );
    } else {
      this.runAction(
        this.userActionsService.reserveBook(book.bookId),
        { message: `You reserved "${book.title}".`, icon: 'calendar-check' },
        `Couldn't reserve "${book.title}".`,
      );
    }
  }

  toggleReadList(book: LibraryBook) {
    const isInReadList =
      this.user?.readList?.some((item) => +item.bookId === +book.bookId) ??
      false;
    const obs = isInReadList
      ? this.userActionsService.removeFromFavorites(book.bookId)
      : this.userActionsService.addToFavorites(book.bookId);

    // The reading list lives on the user, so the book list needn't reload
    this.runAction(
      obs,
      isInReadList
        ? { message: `"${book.title}" was removed from your reading list.`, icon: 'bookmark-dash' }
        : { message: `"${book.title}" was added to your reading list.`, icon: 'bookmark-plus' },
      isInReadList
        ? `Couldn't remove "${book.title}" from your reading list.`
        : `Couldn't add "${book.title}" to your reading list.`,
      false,
    );
  }

  // Runs a book action and says how it went in a toast. The server's own
  // explanation (e.g. why a book can't be borrowed) follows the failure text.
  private runAction(
    action: Observable<unknown>,
    success: { message: string; icon: string },
    failure: string,
    reloadBooks = true,
  ) {
    action.subscribe({
      next: () => {
        this.toast.success(success.message, success.icon);
        this.authService.refreshCurrentUser();
        if (reloadBooks) this.getAllBooks();
      },
      error: (err) => {
        const reason: string | undefined = err?.error?.errorMessages?.[0];
        this.toast.error(reason ? `${failure} ${reason}` : failure);
      },
    });
  }

  retry() {
    this.loading = true;
    this.loadError = false;
    this.getAllBooks();
  }
  closeBook() {
    this.selectedBook = null;
  }

  onGenreSelected(genre: GenreEnums) {
    this.selectedGenre = genre;
    this.updateQueryParams();
  }

  // When arriving with ?genre=…, centre that genre in the strip
  ngAfterViewInit() {
    if (this.selectedGenre !== GenreEnums.All) {
      this.genreStrip?.centerOn(`[data-genre="${this.selectedGenre}"]`);
    }
  }
}
