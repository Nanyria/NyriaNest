import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

// "Showing 11–20 of 42 books" and the page buttons, under a list.
// Doesn't know where the items come from: Manage books pages on the server,
// My owned books in the browser. The page tells it where it is and listens
// for (pageChange), usually to put the page in the URL.
@Component({
  selector: 'app-pager',
  standalone: true,
  templateUrl: './pager.component.html',
  styleUrls: ['./pager.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class PagerComponent {
  readonly page = input.required<number>(); // 1-based
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly itemsOnPage = input.required<number>();
  readonly itemLabel = input('books'); // plural: "of 42 books"
  readonly pageChange = output<number>();

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
  readonly first = computed(() => (this.page() - 1) * this.pageSize() + 1);
  readonly last = computed(() => (this.page() - 1) * this.pageSize() + this.itemsOnPage());

  // Page buttons: first, last, and the current page with its neighbours.
  // null marks a gap ("…").
  readonly pageButtons = computed(() => {
    const current = this.page();
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

  goTo(page: number) {
    if (page !== this.page()) this.pageChange.emit(page);
  }
}
