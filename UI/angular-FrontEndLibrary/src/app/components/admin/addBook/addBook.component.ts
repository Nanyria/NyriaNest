import { Component, OnInit, ChangeDetectionStrategy, ElementRef, HostListener } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Observable } from 'rxjs';

import { BookService } from '../../../Services/book.services';
import { HasUnsavedChanges } from '../../../Services/unsaved-changes.guard';
import { ToastService } from '../../../Services/toast.service';
import { ConfirmService } from '../../../Services/confirm.service';
import { Book, SlimBookDto } from '../../../Models/interfaces';
import {
  GenreEnums,
  GenreDisplayNames,
  LanguageEnums,
  LanguageDisplayNames,
  BookTypeEnums,
  BookTypeDisplayNames,
} from '../../../Helpers/Enums/enum';

type BookFormat = 'physical' | 'digital';
type LoadState = 'loading' | 'error' | 'ready';

const MAX_EPUB_SIZE = 100 * 1024 * 1024; // same limit as EpubService.MaxFileSize

// Checks name and size only; the server still validates the EPUB signature
function epubFileError(file: File): string | null {
  if (!file.name.toLowerCase().endsWith('.epub')) {
    return 'Only .epub files are allowed.';
  }
  if (file.size > MAX_EPUB_SIZE) {
    return 'The file is larger than 100 MB.';
  }
  return null;
}

function epubFileValidator(control: AbstractControl): ValidationErrors | null {
  const file = control.value as File | null;
  if (!file) return null;
  const error = epubFileError(file);
  return error ? { epubFile: error } : null;
}

// A required field: the form controls behind it, and what to focus when it's wrong
interface FieldRef {
  controls: string[];
  focus: string;
}

// In page order. Format and type count as one field, since one choice fixes both.
const FIELDS: FieldRef[] = [
  { controls: ['format', 'bookType'], focus: '.add-book__format .add-book__option' },
  { controls: ['title'], focus: '#title' },
  { controls: ['author'], focus: '#author' },
  { controls: ['publicationYear'], focus: '#publicationYear' },
  { controls: ['genre'], focus: '#genre' },
  { controls: ['language'], focus: '#language' },
  { controls: ['epubFile'], focus: '.add-book__files .add-book__file-input' },
];

// One page for both /add-book and /admin/manage-books/:id/edit; the route decides the mode
@Component({
  selector: 'app-addbook',
  standalone: true,
  templateUrl: './addBook.component.html',
  styleUrls: ['./addBook.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [ReactiveFormsModule, RouterModule],
})
export class AddBookComponent implements OnInit, HasUnsavedChanges {
  addBookForm!: FormGroup;

  // Set when editing, null when adding
  bookId: string | null = null;
  loadState: LoadState = 'ready';

  readonly BookTypeEnums = BookTypeEnums;
  readonly GenreDisplayNames = GenreDisplayNames;
  readonly LanguageDisplayNames = LanguageDisplayNames;
  // "None" and "All" are filter values, not real genres
  readonly genreOptions = (
    Object.values(GenreEnums).filter((v) => typeof v === 'number') as GenreEnums[]
  ).filter((g) => g !== GenreEnums.None && g !== GenreEnums.All);
  readonly languageOptions = (
    Object.values(LanguageEnums).filter((v) => typeof v === 'number') as LanguageEnums[]
  ).filter((l) => l !== LanguageEnums.Undefined);

  isSaving = false;
  submitAttempted = false;
  serverErrors: string[] = [];
  bannerTitle = '';
  private saved = false; // lets the page close without the unsaved-changes question

  // Edit mode: what the book has on the server right now
  hasStoredEpub = false;
  private storedBookType: BookTypeEnums | null = null;

  // EPUB drop area
  isDraggingEpub = false;
  rejectedEpubName = '';
  epubError = '';

  coverPreviewFailed = false;

  constructor(
    private fb: FormBuilder,
    private bookService: BookService,
    private router: Router,
    private route: ActivatedRoute,
    private toast: ToastService,
    private confirmDialog: ConfirmService,
    private host: ElementRef<HTMLElement>,
  ) {}

  ngOnInit(): void {
    this.bookId = this.route.snapshot.paramMap.get('id');

    this.addBookForm = this.fb.group({
      // UI only: decides which book types are offered, never sent to the API
      format: [null as BookFormat | null, Validators.required],
      bookType: [null as BookTypeEnums | null, Validators.required],
      title: ['', [Validators.required, Validators.maxLength(100)]],
      author: ['', [Validators.required, Validators.maxLength(75)]],
      publicationYear: ['', [Validators.required, Validators.pattern('^[0-9]{4}$')]],
      genre: [null as GenreEnums | null, Validators.required],
      language: [LanguageEnums.English, Validators.required],
      bookDescription: [''],
      coverImagePath: [''],
      epubFile: [null as File | null],
      // Edit mode: the admin removed the stored EPUB; it's deleted on save
      removeStoredEpub: [false],
    });

    // The EPUB is required for new e-books only; any other type drops the file.
    // When editing, the details can be saved without one (the catalogue flags it).
    this.addBookForm.get('bookType')!.valueChanges.subscribe((type) => {
      const epub = this.addBookForm.get('epubFile')!;
      if (type === BookTypeEnums.EBook) {
        epub.setValidators(this.isEdit ? [epubFileValidator] : [Validators.required, epubFileValidator]);
      } else {
        epub.clearValidators();
        epub.setValue(null);
        this.clearEpubError();
      }
      epub.updateValueAndValidity();
    });

    this.addBookForm.get('coverImagePath')!.valueChanges.subscribe(() => {
      this.coverPreviewFailed = false;
    });

    if (this.bookId) {
      this.loadBook(this.bookId);
    }
  }

  get isEdit(): boolean {
    return this.bookId !== null;
  }

  private loadBook(id: string): void {
    this.loadState = 'loading';
    this.bookService.getBookById(id).subscribe({
      next: (res) => (res.isSuccess ? this.fillForm(res.result) : (this.loadState = 'error')),
      error: () => (this.loadState = 'error'),
    });
  }

  // reset() instead of patchValue() so the loaded values count as unchanged.
  // Old books may lack a genre, language or type; those start empty so the admin picks one.
  private fillForm(book: Book): void {
    const type = book.bookType === BookTypeEnums.Undefined ? null : book.bookType;
    const digital = type === BookTypeEnums.EBook || type === BookTypeEnums.Audiobook;
    this.addBookForm.reset({
      format: type === null ? null : digital ? 'digital' : 'physical',
      bookType: type,
      title: book.title,
      author: book.author,
      publicationYear: String(book.publicationYear),
      genre: book.genre === GenreEnums.None ? null : book.genre,
      language: book.language === LanguageEnums.Undefined ? null : book.language,
      bookDescription: book.bookDescription ?? '',
      coverImagePath: book.coverImagePath ?? '',
      epubFile: null,
      removeStoredEpub: false,
    });
    this.hasStoredEpub = book.hasEpub;
    this.storedBookType = book.bookType;
    this.loadState = 'ready';
  }

  hasUnsavedChanges(): boolean {
    return !this.saved && this.addBookForm.dirty;
  }

  // Closing or reloading the tab; the browser shows its own "Leave site?" question
  @HostListener('window:beforeunload', ['$event'])
  warnBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  get format(): BookFormat | null {
    return this.addBookForm.get('format')!.value;
  }

  get bookType(): BookTypeEnums | null {
    return this.addBookForm.get('bookType')!.value;
  }

  get isEbook(): boolean {
    return this.bookType === BookTypeEnums.EBook;
  }

  get epubFile(): File | null {
    return this.addBookForm.get('epubFile')!.value;
  }

  get removeStoredEpub(): boolean {
    return this.addBookForm.get('removeStoredEpub')!.value;
  }

  // Edit mode: an e-book with a file is being changed to another type,
  // so saving will delete the file
  get epubWillBeDeleted(): boolean {
    return this.hasStoredEpub && this.bookType !== null && !this.isEbook;
  }

  get coverUrl(): string {
    return (this.addBookForm.get('coverImagePath')!.value ?? '').trim();
  }

  // The fields that block saving, in page order. Each field's own message
  // explains what's wrong; this drives the summary and the jump to the first one.
  get invalidFields(): FieldRef[] {
    return FIELDS.filter((f) => f.controls.some((name) => this.addBookForm.get(name)!.invalid));
  }

  // Format and type are picked with buttons, which never mark the controls as
  // touched, so their message only appears after a save attempt
  get showFormatError(): boolean {
    return this.showError('format') || this.showError('bookType');
  }

  // A save was tried and something still blocks it
  get hasBlockingErrors(): boolean {
    return this.submitAttempted && this.invalidFields.length > 0;
  }

  // Next to the save button. The button stays clickable (except when nothing
  // changed in edit mode), so this only sums up what a save attempt found.
  get saveHint(): string {
    if (this.isEdit && this.addBookForm.pristine) return 'No changes to save yet.';
    if (!this.hasBlockingErrors) return '';
    const count = this.invalidFields.length;
    return count === 1 ? '1 field needs attention.' : `${count} fields need attention.`;
  }

  // Picking a format preselects its first type.
  // patchValue() doesn't mark the form as changed, so these do it themselves.
  setFormat(format: BookFormat): void {
    if (this.format === format) return;
    this.addBookForm.patchValue({
      format,
      bookType: format === 'physical' ? BookTypeEnums.Hardcover : BookTypeEnums.EBook,
    });
    this.addBookForm.markAsDirty();
  }

  setBookType(type: BookTypeEnums): void {
    if (this.bookType === type) return;
    this.addBookForm.patchValue({ bookType: type });
    this.addBookForm.markAsDirty();
  }

  showError(name: string): boolean {
    const control = this.addBookForm.get(name)!;
    return control.invalid && (control.dirty || control.touched);
  }

  // ---------- EPUB file ----------

  onEpubSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.takeEpub(input.files?.[0] ?? null);
    input.value = ''; // lets the same file be picked again after Remove
  }

  onEpubDragOver(event: DragEvent): void {
    event.preventDefault(); // required, otherwise the browser never fires drop
    this.isDraggingEpub = true;
  }

  onEpubDragLeave(): void {
    this.isDraggingEpub = false;
  }

  onEpubDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDraggingEpub = false;
    this.takeEpub(event.dataTransfer?.files?.[0] ?? null);
  }

  // A file dropped next to a drop area would make the browser open it and
  // throw the form away; outside the drop areas a drop does nothing instead
  @HostListener('window:dragover', ['$event'])
  @HostListener('window:drop', ['$event'])
  preventFileOpen(event: DragEvent): void {
    event.preventDefault();
    const target = event.target as HTMLElement | null;
    if (event.dataTransfer && !target?.closest('.add-book__drop:not(.add-book__drop--disabled), .add-book__file')) {
      event.dataTransfer.dropEffect = 'none';
    }
  }

  removeEpub(): void {
    this.addBookForm.get('epubFile')!.setValue(null);
    this.clearEpubError();
  }

  // Edit mode: nothing is deleted until the admin saves
  setRemoveStoredEpub(remove: boolean): void {
    this.addBookForm.patchValue({ removeStoredEpub: remove });
    this.addBookForm.markAsDirty();
  }

  private takeEpub(file: File | null): void {
    if (!file) return;
    const error = epubFileError(file);
    if (error) {
      this.epubError = error;
      this.rejectedEpubName = file.name;
      this.addBookForm.get('epubFile')!.setValue(null);
      return;
    }
    this.clearEpubError();
    this.addBookForm.get('epubFile')!.setValue(file);
    this.addBookForm.markAsDirty();
  }

  private clearEpubError(): void {
    this.epubError = '';
    this.rejectedEpubName = '';
  }

  formatSize(bytes: number): string {
    if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  // ---------- Save ----------

  async onSubmit(): Promise<void> {
    if (this.isSaving) return;
    if (this.addBookForm.invalid) {
      this.submitAttempted = true;
      this.addBookForm.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }

    if (this.epubWillBeDeleted) {
      const typeName = BookTypeDisplayNames[this.storedBookType!].toLowerCase();
      const confirmed = await this.confirmDialog.confirm({
        title: `Delete the attached ${typeName} file?`,
        message: `Saving this book as ${BookTypeDisplayNames[this.bookType!]} will delete its EPUB file. Are you sure you want to proceed?`,
        confirmLabel: 'Delete file and save',
        danger: true,
        icon: 'trash3',
      });
      if (!confirmed) return;
    }

    const value = this.addBookForm.value;
    const book: SlimBookDto = {
      title: value.title.trim(),
      author: value.author.trim(),
      genre: Number(value.genre) as GenreEnums,
      bookType: Number(value.bookType) as BookTypeEnums,
      language: Number(value.language) as LanguageEnums,
      publicationYear: value.publicationYear,
      bookDescription: value.bookDescription?.trim() || undefined,
      coverImagePath: this.coverUrl || undefined,
    };

    this.isSaving = true;
    this.serverErrors = [];

    if (this.bookId) {
      this.saveChanges(this.bookId, book);
      return;
    }

    const request = this.isEbook
      ? this.bookService.addEbook(book, this.epubFile!)
      : this.bookService.addBook(book);

    request.subscribe({
      next: () => this.finish(`"${book.title}" was added.`),
      error: (err) => this.showServerErrors(err, 'The book could not be saved.'),
    });
  }

  // Scrolls the first problem into view and puts the cursor there.
  // After a tick, so the error messages are rendered and the layout is final.
  private focusFirstInvalid(): void {
    const first = this.invalidFields[0];
    if (!first) return;
    setTimeout(() => {
      const target = this.host.nativeElement.querySelector<HTMLElement>(first.focus);
      target?.scrollIntoView({ block: 'center', behavior: 'smooth' });
      target?.focus({ preventScroll: true });
    });
  }

  // The details and the file are separate requests: details first, then the
  // file. If only the file fails, the details stay saved and the page says so.
  private saveChanges(id: string, book: SlimBookDto): void {
    this.bookService.updateBook(id, book).subscribe({
      next: () => {
        // The API drops the file when an e-book becomes another type
        this.storedBookType = book.bookType ?? null;
        if (!this.isEbook) this.hasStoredEpub = false;

        const fileRequest = this.epubRequest(id);
        if (!fileRequest) {
          this.finish(`"${book.title}" was saved.`);
          return;
        }
        fileRequest.subscribe({
          next: () => this.finish(`"${book.title}" was saved.`),
          error: (err) =>
            this.showServerErrors(err, 'The details were saved, but the EPUB file could not be updated.'),
        });
      },
      error: (err) => this.showServerErrors(err, 'The book could not be saved.'),
    });
  }

  // A new file replaces the stored one; otherwise a removed file is deleted
  private epubRequest(id: string): Observable<unknown> | null {
    if (!this.isEbook) return null;
    if (this.epubFile) return this.bookService.uploadEpub(id, this.epubFile);
    if (this.hasStoredEpub && this.removeStoredEpub) return this.bookService.deleteEpub(id);
    return null;
  }

  // Back to the list, with a toast saying what happened
  private finish(message: string): void {
    this.isSaving = false;
    this.saved = true;
    this.toast.success(message);
    this.router.navigate(['/admin/manage-books']);
  }

  private showServerErrors(err: any, title: string): void {
    this.isSaving = false;
    this.bannerTitle = title;
    const messages: string[] = err?.error?.errorMessages ?? [];
    this.serverErrors = messages.length ? messages : ['Something went wrong. Please try again.'];
  }
}
