import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';

export type ReasonKind = 'edit' | 'delete' | 'removed' | 'lost' | 'restore';

export const OTHER_REASON = 'Other';

// The reasons an admin picks from when acting on someone else's book (#83).
// The API takes any text, so these can change freely. "Other" asks for their own words.
export const REASON_PRESETS: Record<ReasonKind, string[]> = {
  edit: ['Wrong or missing details', 'Inappropriate content'],
  delete: ['Duplicate', 'Not a real book / spam', 'Inappropriate content'],
  removed: ['Inappropriate content', 'Not a real book / spam', 'Duplicate'],
  lost: ['Reported lost by the owner'],
  restore: ['Removed by mistake', 'The problem is fixed'],
};

// Same limit as the API (BookEditRequest.Reason and the others)
export const REASON_MAX_LENGTH = 200;

export interface ReasonDialogOptions {
  kind: ReasonKind;
  title: string;
  message?: string;
  confirmLabel: string;
  icon: string; // Bootstrap icon name without "bi-"
  danger?: boolean;
  // Runs with the chosen reason. The dialog stays open on failure and shows the API's message.
  action: (reason: string) => Observable<unknown>;
  fallbackError: string;
}

// The confirm dialog with a reason: a list of presets, and a text field for "Other".
// The reason goes to the owner in their notification, so the admin has to give one.
// Opened by rendering it; tells the page it closed through "closed" (true = done).
@Component({
  selector: 'app-reason-dialog',
  standalone: true,
  templateUrl: './reason-dialog.component.html',
  styleUrls: ['../confirm-dialog/confirm-dialog.component.css', './reason-dialog.component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [ReactiveFormsModule],
})
export class ReasonDialogComponent {
  private dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  readonly options = input.required<ReasonDialogOptions>();
  readonly closed = output<boolean>();

  readonly other = OTHER_REASON;
  readonly maxLength = REASON_MAX_LENGTH;
  readonly saving = signal(false);
  readonly submitted = signal(false);
  readonly error = signal<string | null>(null);

  readonly preset = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly otherText = new FormControl('', {
    nonNullable: true,
    validators: [Validators.maxLength(REASON_MAX_LENGTH)],
  });

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.showModal());
  }

  presets(): string[] {
    return REASON_PRESETS[this.options().kind];
  }

  isOther(): boolean {
    return this.preset.value === OTHER_REASON;
  }

  // "Other" needs words of its own; whitespace doesn't count
  otherMissing(): boolean {
    return this.isOther() && !this.otherText.value.trim();
  }

  save(): void {
    this.submitted.set(true);
    this.error.set(null);
    if (this.preset.invalid || this.otherMissing() || this.otherText.invalid) return;

    const reason = this.isOther() ? this.otherText.value.trim() : this.preset.value;
    this.saving.set(true);
    this.options().action(reason).subscribe({
      next: () => this.close(true),
      error: (err) => {
        this.saving.set(false);
        this.error.set(err?.error?.errorMessages?.[0] ?? this.options().fallbackError);
      },
    });
  }

  // Tells the page once, however the dialog was closed (button, Esc, backdrop)
  private finished = false;

  close(done: boolean): void {
    if (this.finished) return;
    this.finished = true;
    const dialog = this.dialog().nativeElement;
    if (dialog.open) dialog.close();
    this.closed.emit(done);
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
