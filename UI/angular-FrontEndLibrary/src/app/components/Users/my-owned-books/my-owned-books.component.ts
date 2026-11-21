import { ChangeDetectionStrategy, Component, HostListener, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { catchError, map, startWith, switchMap } from 'rxjs/operators';
import { MyBook, StatusHistoryItem } from '../../../Models/interfaces';
import { MyBooksService } from '../../../Services/my-books.service';
import { ToastService } from '../../../Services/toast.service';
import { ConfirmService } from '../../../Services/confirm.service';
import { BookStatusDisplayNames, BookStatusEnum, BookTypeDisplayNames } from '../../../Helpers/Enums/enum';
import { LoanDialogComponent, LoanDialogMode } from './loan-dialog.component';
import { dateOnly, formatDay, formatMoment, todayIso } from './loan-dates';

type ViewState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'ready'; books: MyBook[] };

// The history open under one row
type HistoryState =
  | { bookId: number; kind: 'loading' }
  | { bookId: number; kind: 'error' }
  | { bookId: number; kind: 'ready'; items: StatusHistoryItem[]; showAll: boolean };

const HISTORY_PREVIEW = 5;

// The books on the user's own shelf, and who has borrowed them (#83).
// Laid out like Manage books.
@Component({
  selector: 'app-my-owned-books',
  standalone: true,
  templateUrl: './my-owned-books.component.html',
  styleUrls: ['./my-owned-books.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [RouterLink, LoanDialogComponent],
  host: { class: 'nn-compact' },
})
export class MyOwnedBooksComponent {
  private myBooks = inject(MyBooksService);
  private toast = inject(ToastService);
  private confirmDialog = inject(ConfirmService);
  private refresh$ = new BehaviorSubject<void>(undefined);

  readonly bookTypeNames = BookTypeDisplayNames;
  readonly historyPreview = HISTORY_PREVIEW;
  readonly fallbackCover = 'assets/images/book-covers/BrownBook.svg';
  readonly formatMoment = formatMoment;

  readonly state = toSignal(
    this.refresh$.pipe(
      switchMap(() =>
        this.myBooks.getAll().pipe(
          map((res): ViewState => (res.isSuccess ? { kind: 'ready', books: res.result } : { kind: 'error' })),
          catchError(() => of<ViewState>({ kind: 'error' })),
          startWith<ViewState>({ kind: 'loading' }),
        ),
      ),
    ),
    { initialValue: { kind: 'loading' } as ViewState },
  );

  // Shown above the list when an action fails
  readonly actionError = signal<string | null>(null);
  readonly history = signal<HistoryState | null>(null);
  readonly loanDialog = signal<{ book: MyBook; mode: LoanDialogMode } | null>(null);

  retry() {
    this.refresh$.next();
  }

  // ---------- Status ----------
  isWithdrawn(b: MyBook) {
    return b.adminStatus !== null;
  }

  isLent(b: MyBook) {
    return !!b.lentTo;
  }

  isOverdue(b: MyBook) {
    return !!b.dueDate && dateOnly(b.dueDate) < todayIso();
  }

  statusLabel(b: MyBook): string | null {
    if (b.adminStatus === BookStatusEnum.Lost) return 'Lost';
    if (b.adminStatus === BookStatusEnum.Removed) return 'Removed by an admin';
    if (this.isLent(b)) return `Lent to ${b.lentTo}`;
    return null; // on the shelf: no tag
  }

  statusClass(b: MyBook): string {
    if (this.isWithdrawn(b)) return 'nn-tag--withdrawn';
    return this.isOverdue(b) ? 'nn-tag--overdue' : 'nn-tag--checked-out';
  }

  loanDetail(b: MyBook): string {
    return this.isLent(b) ? `since ${formatDay(b.lentOn)} · due ${formatDay(b.dueDate)}` : '';
  }

  // ---------- ⋯ menu (same popover as Manage books) ----------
  placeMenu(event: Event, button: HTMLElement) {
    if ((event as ToggleEvent).newState !== 'open') return;
    const menu = event.target as HTMLElement;
    const r = button.getBoundingClientRect();
    const openUp = r.bottom > window.innerHeight - 180;
    menu.style.right = `${document.documentElement.clientWidth - r.right}px`;
    menu.style.top = openUp ? 'auto' : `${r.bottom + 4}px`;
    menu.style.bottom = openUp ? `${window.innerHeight - r.top + 4}px` : 'auto';
  }

  closeMenu(event: Event) {
    (event.currentTarget as HTMLElement).hidePopover();
  }

  @HostListener('window:scroll')
  @HostListener('window:resize')
  closeOpenMenus() {
    document.querySelectorAll<HTMLElement>('.ob-menu__list:popover-open').forEach((m) => m.hidePopover());
  }

  // ---------- Lend / return ----------
  openLoanDialog(book: MyBook, mode: LoanDialogMode) {
    this.actionError.set(null);
    this.loanDialog.set({ book, mode });
  }

  onLoanDialogClosed(saved: boolean) {
    const open = this.loanDialog();
    this.loanDialog.set(null);
    if (!saved || !open) return;
    this.toast.success(
      open.mode === 'lend' ? `"${open.book.title}" is lent out.` : `"${open.book.title}" is back on your shelf.`,
      open.mode === 'lend' ? 'box-arrow-up-right' : 'box-arrow-in-down-left',
    );
    this.afterChange(open.book.bookId);
  }

  // ---------- History ----------
  isHistoryOpen(b: MyBook) {
    return this.history()?.bookId === b.bookId;
  }

  toggleHistory(b: MyBook) {
    if (this.isHistoryOpen(b)) {
      this.history.set(null);
    } else {
      this.loadHistory(b.bookId);
    }
  }

  showAllHistory() {
    const h = this.history();
    if (h?.kind === 'ready') this.history.set({ ...h, showAll: true });
  }

  historyText(item: StatusHistoryItem): string {
    return item.notes || BookStatusDisplayNames[item.bookStatus];
  }

  loadHistory(bookId: number) {
    this.history.set({ bookId, kind: 'loading' });
    this.myBooks.getHistory(bookId).subscribe({
      next: (res) => {
        if (this.history()?.bookId !== bookId) return; // another row was opened meanwhile
        this.history.set(
          res.isSuccess ? { bookId, kind: 'ready', items: res.result, showAll: false } : { bookId, kind: 'error' },
        );
      },
      error: () => {
        if (this.history()?.bookId === bookId) this.history.set({ bookId, kind: 'error' });
      },
    });
  }

  // ---------- Delete ----------
  async delete(b: MyBook) {
    this.actionError.set(null);
    const lent = this.isLent(b);
    const confirmed = await this.confirmDialog.confirm({
      title: `Delete "${b.title}"?`,
      message: lent
        ? `It's marked as lent to ${b.lentTo}. Delete it anyway? The loan is removed too.`
        : "This can't be undone.",
      confirmLabel: 'Delete',
      danger: true,
      icon: 'trash3',
    });
    if (!confirmed) return;

    // A loan the list didn't know about yet still gets a 409 with the API's message
    this.myBooks.delete(b.bookId, lent).subscribe({
      next: () => {
        this.toast.success(`"${b.title}" was deleted.`, 'trash3');
        if (this.history()?.bookId === b.bookId) this.history.set(null);
        this.refresh$.next();
      },
      error: (err) => this.actionError.set(err?.error?.errorMessages?.[0] ?? `Couldn't delete "${b.title}".`),
    });
  }

  // Reload the list, and the open history if it's this book's
  private afterChange(bookId: number) {
    this.refresh$.next();
    if (this.history()?.bookId === bookId) this.loadHistory(bookId);
  }
}
