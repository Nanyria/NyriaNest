import {
  AfterViewInit,
  Component,
  DestroyRef,
  HostListener,
  Input,
  ViewChild,
  ChangeDetectionStrategy,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';

import { LibraryBook, ILoggedInUser } from '../../../Models/interfaces';
import { LibraryService } from '../../../Services/library.services';
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
  InfoCardColumn2,
  InfoCardColumn3,
  InfoCardDisplayRow,
} from '../../../Models/interfaces/site-interfaces';
import { InfoCardComponent } from '../../card-components/info-card/info-card.component';
import { ScrollStripComponent } from '../../layout/scroll-strip/scroll-strip.component';
import {
  BookTypeOptions,
  BookStatusOptions,
  GenreOptions,
} from '../../../Helpers/Helper';
import {
  GENRE_CARD_STYLES,
  GenreCardStyle,
} from '../../../Helpers/genre-styles/genre-cards';

type StatusFilter = 'all' | 'available' | 'reserved' | 'checkedOut';
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
    InfoCardComponent,
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
    format: 'all' as BookFormatFilter,
    minimumRating: 0,
  };

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
  showInfoCard = false;
  selectedBook: LibraryBook | null = null;
  selectedBookReviews: any[] = [];
  infoCardActions: { label: string; action: () => void; disabled?: boolean }[] =
    [];
  isSuperAdmin: boolean = false;
  bookTypeOptions = BookTypeOptions;
  bookStatusOptions = BookStatusOptions;
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
  ) {}
  @ViewChild(InfoCardComponent) infoCardComponent?: InfoCardComponent;

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
          this.updateInfoCardActions(this.selectedBook);
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
            this.updateInfoCardActions(fresh);
          }
        }
      },
      (error) => {
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
      format: 'all',
      minimumRating: 0,
    };
  }

  applyFilters() {
    this.selectedGenre = this.draft.genre;
    this.selectedStatus = this.draft.status;
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
    this.selectedBookFormat = 'all';
    this.selectedMinimumRating = 0;
    this.updateQueryParams();
  }

  // The list already carries everything the popup shows; only reviews are fetched
  onBookRowClicked(book: LibraryBook) {
    this.selectedBook = book;
    this.showInfoCard = true;
    this.updateInfoCardActions(book);
    this.selectedBookReviews = [];
    this.libraryService.getBookReviews(book.bookId).subscribe({
      next: (reviewResponse) => {
        this.selectedBookReviews = (reviewResponse.result || []).map(
          (review: any): InfoCardDisplayRow[] => [
            { label: '', value: review.userName },
            { label: '', value: review.createdAt },
            { label: '', value: `Rating: ${review.ratingItem?.rating}` },
            { isBreak: true, value: '' },
            { label: '', value: review.reviewHeader },
            { label: '', value: review.reviewText },
          ],
        );
      },
      error: () => {
        this.selectedBookReviews = [];
      },
    });
  }

  updateInfoCardActions(book: LibraryBook) {
    const isInReadList =
      this.user?.readList?.some((item) => +item.bookId === +book.bookId) ??
      false;

    this.infoCardActions = [
      {
        label: book.isCheckedOutByMe ? 'Return' : 'Borrow',
        action: () => this.toggleBorrow(book),
        disabled: this.isBorrowDisabled(book),
      },
      {
        label: book.isReservedByMe ? 'Cancel reservation' : 'Reserve',
        action: () => this.toggleReservation(book),
      },
      {
        label: isInReadList ? 'Remove from reading list' : 'Add to reading list',
        action: () => this.toggleReadList(book),
      },
    ];
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
  getBookStatusDisplay(book: LibraryBook): { status: string; availability: string } {
    const status =
      this.bookStatusOptions.displayNames[book.bookStatus]?.toString() ||
      (typeof book.bookStatus === 'string'
        ? book.bookStatus
        : book.bookStatus?.toString() || '');

    // If user has a reservation, show their reservation's availability date
    const date = book.myReservationAvailableDate ?? book.availabilityDate;
    const availabilityDate = date ? new Date(date).toLocaleDateString() : '';

    return {
      status,
      availability: availabilityDate,
    };
  }

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
  closeInfoCard() {
    this.showInfoCard = false;
    this.selectedBook = null;
  }

  get infoCardCol2(): InfoCardColumn2 {
    if (!this.selectedBook) return {};
    const statusObj = this.getBookStatusDisplay(this.selectedBook);
    return {
      rows: [
        { title: 'Title', value: this.selectedBook.title ?? '' },
        { title: 'Author', value: this.selectedBook.author ?? '' },
      ],
      rowPairs: [
        {
          first: {
            title: 'Genre',
            value:
              this.genreOptions.displayNames[
                this.selectedBook.genre
              ]?.toString() ||
              this.selectedBook.genre?.toString() ||
              '',
          },
          second: {
            title: 'Book type',
            value:
              this.bookTypeOptions.displayNames[
                this.selectedBook.bookType
              ]?.toString() || '',
          },
        },
        {
          first: {
            title: 'Published',
            value: this.selectedBook.publicationYear?.toString() || '',
          },
          second: { title: statusObj.status, value: statusObj.availability },
        },
      ],
      longText: {
        title: 'Description',
        value: this.selectedBook.bookDescription ?? '',
      },
    };
  }

  get infoCardCol3(): InfoCardColumn3 {
    if (!this.selectedBook) return {};
    return {
      rows: [
        {
          title: 'Reviews',
          value: this.selectedBookReviews, // This is InfoCardDisplayRow[][]
        },
      ],
    };
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
