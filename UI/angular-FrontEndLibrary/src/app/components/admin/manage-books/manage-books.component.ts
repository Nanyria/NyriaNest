import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { BehaviorSubject, Observable, combineLatest, of } from 'rxjs';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  startWith,
  switchMap,
} from 'rxjs/operators';
import { BookService } from '../../../Services/book.services';
import { ToastService } from '../../../Services/toast.service';
import { ConfirmService } from '../../../Services/confirm.service';
import {
  AdminBook,
  CatalogueQuery,
  CatalogueSort,
  CatalogueStatus,
  PagedResult,
} from '../../../Models/interfaces';
import {
  BookStatusEnum,
  BookTypeDisplayNames,
  BookTypeEnums,
  GenreDisplayNames,
  GenreEnums,
  LanguageDisplayNames,
} from '../../../Helpers/Enums/enum';
import { BookTypeOptions, GenreOptions } from '../../../Helpers/Helper';

type ViewState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'ready'; data: PagedResult<AdminBook> };

const PAGE_SIZE = 10;

// The URL query string is the single source of truth for filters, sorting and
// paging, so refresh and the Back button keep the admin's place.
function readQuery(p: ParamMap): CatalogueQuery {
  const num = (k: string) => (p.has(k) ? Number(p.get(k)) : undefined);
  return {
    q: p.get('q') ?? undefined,
    genre: num('genre'),
    bookType: num('bookType'),
    status: (p.get('status') as CatalogueStatus | null) ?? undefined,
    needsAttention: p.get('needsAttention') === 'true' || undefined,
    sort: (p.get('sort') as CatalogueSort | null) ?? 'titleAsc',
    page: num('page') ?? 1,
    pageSize: PAGE_SIZE,
  };
}

@Component({
  selector: 'app-manage-books',
  standalone: true,
  templateUrl: './manage-books.component.html',
  styleUrls: ['./manage-books.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [ReactiveFormsModule, RouterLink],
  host: { class: 'nn-compact' }, // the theme's tighter, lighter fields, like Add book
})
export class ManageBooksComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private bookService = inject(BookService);
  private toast = inject(ToastService);
  private confirmDialog = inject(ConfirmService);
  private refresh$ = new BehaviorSubject<void>(undefined);

  // "All" means no filter, which the empty option already covers
  readonly genreValues = GenreOptions.values.filter((g) => g !== GenreEnums.All);
  readonly bookTypeValues = BookTypeOptions.values;
  readonly genreNames = GenreDisplayNames;
  readonly bookTypeNames = BookTypeDisplayNames;
  readonly languageNames = LanguageDisplayNames;
  readonly Status = BookStatusEnum;
  readonly fallbackCover = 'assets/images/book-covers/BrownBook.svg';

  readonly query = toSignal(this.route.queryParamMap.pipe(map(readQuery)), {
    initialValue: readQuery(this.route.snapshot.queryParamMap),
  });

  // Reloads whenever the URL changes or refresh$ fires (after an action)
  readonly state = toSignal(
    combineLatest([this.route.queryParamMap, this.refresh$]).pipe(
      switchMap(([params]) =>
        this.bookService.getCatalogue(readQuery(params)).pipe(
          map((res): ViewState =>
            res.isSuccess ? { kind: 'ready', data: res.result } : { kind: 'error' },
          ),
          catchError(() => of<ViewState>({ kind: 'error' })),
          startWith<ViewState>({ kind: 'loading' }),
        ),
      ),
    ),
    { initialValue: { kind: 'loading' } as ViewState },
  );

  // The current page, or null while loading or after an error
  readonly page = computed(() => {
    const s = this.state();
    return s.kind === 'ready' ? s.data : null;
  });

  readonly totalPages = computed(() => {
    const p = this.page();
    return p ? Math.max(1, Math.ceil(p.totalCount / p.pageSize)) : 1;
  });

  // Page buttons: first, last, and the current page with its neighbours.
  // null marks a gap ("…").
  readonly pageButtons = computed(() => {
    const current = this.page()?.page ?? 1;
    const last = this.totalPages();
    const pages: (number | null)[] = [];
    for (let n = 1; n <= last; n++) {
      if (n === 1 || n === last || Math.abs(n - current) <= 1) {
        pages.push(n);
      } else if (pages[pages.length - 1] !== null) {
        pages.push(null);
      }
    }
    return pages;
  });

  readonly hasFilters = computed(() => {
    const q = this.query();
    return !!(q.q || q.genre !== undefined || q.bookType !== undefined || q.status || q.needsAttention);
  });

  // Shown above the list when an action (mark lost, delete, …) fails
  readonly actionError = signal<string | null>(null);

  readonly search = new FormControl(this.query().q ?? '', { nonNullable: true });

  constructor() {
    // Wait for a pause in typing before searching
    this.search.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((q) => this.setFilter({ q: q.trim() || null }));
  }

  /** Updates the URL. null removes a param. Any filter change goes back to page 1. */
  setFilter(patch: Record<string, string | number | boolean | null>) {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { ...patch, page: null },
      queryParamsHandling: 'merge',
    });
  }

  goToPage(page: number) {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: page === 1 ? null : page },
      queryParamsHandling: 'merge',
    });
  }

  clearAll() {
    this.search.setValue('', { emitEvent: false });
    this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  retry() {
    this.refresh$.next();
  }
  // Status badge text. Overdue isn't a stored status: it's a checked-out book past its due date.
  statusLabel(b: AdminBook): string {
    switch (b.bookStatus) {
      case BookStatusEnum.CheckedOut: return b.isOverdue ? 'Overdue' : 'Checked out';
      case BookStatusEnum.Reserved: return 'Reserved';
      case BookStatusEnum.Lost: return 'Lost';
      case BookStatusEnum.Removed: return 'Removed';
      default: return 'Available';
    }
  }

  statusClass(b: AdminBook): string {
    switch (b.bookStatus) {
      case BookStatusEnum.CheckedOut: return b.isOverdue ? 'nn-tag--overdue' : 'nn-tag--checked-out';
      case BookStatusEnum.Reserved: return 'nn-tag--reserved';
      case BookStatusEnum.Lost:
      case BookStatusEnum.Removed: return 'nn-tag--withdrawn';
      default: return 'nn-tag--available';
    }
  }

  // The small line under the badge: who has it, who's next, or the admin's note
  statusDetail(b: AdminBook): string {
    const date = (s?: string | null) =>
      s ? new Date(s).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' }) : '';

    if (b.borrowerName) {
      if (b.isOverdue && b.dueDate) {
        const days = Math.ceil((Date.now() - new Date(b.dueDate).getTime()) / 86_400_000);
        return `${b.borrowerName} · ${days} day${days === 1 ? '' : 's'} overdue`;
      }
      return `${b.borrowerName} · due ${date(b.dueDate)}`;
    }
    // The date is an estimate of when the book reaches them, not a hold expiry
    if (b.nextInLineName) {
      return b.expectedDate
        ? `Next in line: ${b.nextInLineName} · expected ${date(b.expectedDate)}`
        : `Next in line: ${b.nextInLineName}`;
    }
    return b.adminStatusNotes ?? '';
  }

  isEbook(b: AdminBook) {
    return b.bookType === BookTypeEnums.EBook;
  }

  isWithdrawn(b: AdminBook) {
    return b.bookStatus === BookStatusEnum.Lost || b.bookStatus === BookStatusEnum.Removed;
  }

  // The ⋯ menu is a popover: it opens in the browser's top layer, so the
  // table's scroll area can't clip it or make the page scroll. Place it under
  // its button, or above it when the button is near the bottom of the window.
  placeMenu(event: Event, button: HTMLElement) {
    if ((event as ToggleEvent).newState !== 'open') return;
    const menu = event.target as HTMLElement;
    const r = button.getBoundingClientRect();
    const openUp = r.bottom > window.innerHeight - 180;
    menu.style.right = `${document.documentElement.clientWidth - r.right}px`;
    menu.style.top = openUp ? 'auto' : `${r.bottom + 4}px`;
    menu.style.bottom = openUp ? `${window.innerHeight - r.top + 4}px` : 'auto';
  }

  // Close once an action is picked
  closeMenu(event: Event) {
    (event.currentTarget as HTMLElement).hidePopover();
  }

  // The menu is fixed to the window, so it would drift away from its button
  @HostListener('window:scroll')
  @HostListener('window:resize')
  closeOpenMenus() {
    document.querySelectorAll<HTMLElement>('.mb-menu__list:popover-open').forEach((m) => m.hidePopover());
  }

  markLost(b: AdminBook) {
    this.confirmAdminStatus(b, BookStatusEnum.Lost);
  }

  markRemoved(b: AdminBook) {
    this.confirmAdminStatus(b, BookStatusEnum.Removed);
  }

  restore(b: AdminBook) {
    this.run(
      this.bookService.clearAdminStatus(b.bookId),
      { message: `"${b.title}" was restored.`, icon: 'arrow-counterclockwise' },
      `Couldn't restore "${b.title}".`,
    );
  }

  async delete(b: AdminBook) {
    const confirmed = await this.confirmDialog.confirm({
      title: `Delete "${b.title}"?`,
      message: b.hasEpub ? "This can't be undone. Its EPUB file will be deleted too." : "This can't be undone.",
      confirmLabel: 'Delete',
      danger: true,
      icon: 'trash3',
    });
    if (!confirmed) return;
    this.run(
      this.bookService.deleteBook(String(b.bookId)),
      { message: `"${b.title}" was deleted.`, icon: 'trash3' },
      `Couldn't delete "${b.title}".`,
    );
  }

  // Not shown as dangerous: Restore undoes it
  private async confirmAdminStatus(b: AdminBook, status: BookStatusEnum.Lost | BookStatusEnum.Removed) {
    const label = status === BookStatusEnum.Lost ? 'lost' : 'removed';
    const icon = status === BookStatusEnum.Lost ? 'question-circle' : 'archive';
    const confirmed = await this.confirmDialog.confirm({
      title: `Mark "${b.title}" as ${label}?`,
      message: "It can't be borrowed or reserved until you restore it.",
      confirmLabel: `Mark as ${label}`,
      icon,
    });
    if (!confirmed) return;
    this.run(
      this.bookService.setAdminStatus(b.bookId, status),
      { message: `"${b.title}" was marked as ${label}.`, icon },
      `Couldn't mark "${b.title}" as ${label}.`,
    );
  }

  // Runs an action, reloads the list and shows a toast. On failure, shows the
  // API's own message if there is one (e.g. "Return this book before deleting it.").
  // The toast icon matches the menu item's icon.
  private run(
    action: Observable<unknown>,
    success: { message: string; icon: string },
    fallbackMessage: string,
  ) {
    this.actionError.set(null);
    action.subscribe({
      next: () => {
        this.toast.success(success.message, success.icon);
        this.refresh$.next();
      },
      error: (err) => this.actionError.set(err?.error?.errorMessages?.[0] ?? fallbackMessage),
    });
  }
}
