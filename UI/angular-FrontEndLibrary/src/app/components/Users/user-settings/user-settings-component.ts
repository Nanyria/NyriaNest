import {
  Component,
  OnInit,
  ChangeDetectionStrategy,
  DestroyRef,
  ElementRef,
  inject,
  HostListener,
  ViewChild,
} from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { take } from 'rxjs';
import { ILoggedInUser } from '../../../Models/interfaces';
import { AuthService } from '../../../Services/auth.service';
import { UserService } from '../../../Services/user.service';
import { UserActionsService } from '../../../Services/user-actions.service';
import { ToastService } from '../../../Services/toast.service';
import { HasUnsavedChanges } from '../../../Services/unsaved-changes.guard';
import { PASSWORD_RULES, passwordRulesValidator } from '../../../Helpers/password-rules';
import { PictureError, prepareProfilePicture } from '../../../Helpers/profile-picture';

type LoadState = 'loading' | 'ready' | 'error';
type Card = 'profile' | 'email' | 'password';
type AccountRow = 'email' | 'password';
type SectionId = 'profile' | 'security';

// Where focus goes when a row's form opens
const FIRST_FIELD: Record<AccountRow, string> = { email: 'newEmail', password: 'currentPassword' };

// Same limit as the API (UserLimits.BioMaxLength)
const BIO_MAX_LENGTH = 300;

// Required, and not just spaces (the API rejects both)
const notBlank = (control: AbstractControl): ValidationErrors | null =>
  (control.value ?? '').trim() ? null : { required: true };

const passwordsMatch = (group: AbstractControl): ValidationErrors | null => {
  const { newPassword, confirmPassword } = group.value;
  return confirmPassword && newPassword !== confirmPassword ? { mismatch: true } : null;
};

const DEFAULT_PICTURE = 'assets/images/profile-pictures/BlueKidDragon.svg';

// The user's own account: profile, email and password, each saved on its own
@Component({
  selector: 'app-user-settings',
  standalone: true,
  templateUrl: './user-settings-component.html',
  styleUrls: ['./user-settings-component.css'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [ReactiveFormsModule, RouterModule, NgTemplateOutlet],
})
export class UserSettingsComponent implements OnInit, HasUnsavedChanges {
  readonly passwordRules = PASSWORD_RULES;
  readonly bioMaxLength = BIO_MAX_LENGTH;

  // Two pages on wider screens, /settings/profile and /settings/security, with links
  // between them under the header. Phones show both on one page. Delete account is on Security.
  readonly sections: { id: SectionId; label: string; icon: string }[] = [
    { id: 'profile', label: 'Profile', icon: 'person' },
    { id: 'security', label: 'Security', icon: 'shield-lock' },
  ];
  activeSection: SectionId = 'profile';
  private readonly destroyRef = inject(DestroyRef);
  readonly profilePictures = [
    { url: 'assets/images/profile-pictures/BlueKidDragon.svg', label: 'Blue dragon' },
    { url: 'assets/images/profile-pictures/PinkKidDragon.svg', label: 'Pink dragon' },
    { url: 'assets/images/profile-pictures/RedDragon.svg', label: 'Red dragon' },
  ];

  loadState: LoadState = 'loading';
  userName = '';
  currentEmail = '';
  // What the profile card goes back to on Cancel
  private savedProfile = { firstName: '', lastName: '', bio: '' };

  // The picture is saved as soon as it's chosen, so it isn't part of the profile form
  pictureUrl = DEFAULT_PICTURE;
  pictureMenuOpen = false;
  pictureSaving = false;
  @ViewChild('pictureArea') private pictureArea?: ElementRef<HTMLElement>;
  @ViewChild('pictureButton') private pictureButton?: ElementRef<HTMLButtonElement>;

  readonly profileForm = this.fb.nonNullable.group({
    firstName: ['', notBlank],
    lastName: ['', notBlank],
    bio: ['', Validators.maxLength(BIO_MAX_LENGTH)],
  });

  readonly emailForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    currentPassword: ['', Validators.required],
  });

  readonly passwordForm = this.fb.nonNullable.group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, passwordRulesValidator]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatch },
  );

  // Per card: a save is running, a save was tried, and what the API said went wrong
  saving: Record<Card, boolean> = { profile: false, email: false, password: false };
  submitted: Record<Card, boolean> = { profile: false, email: false, password: false };
  apiErrors: Record<Card, string[]> = { profile: [], email: [], password: [] };

  constructor(
    private fb: FormBuilder,
    private userService: UserService,
    private userActionsService: UserActionsService,
    private authService: AuthService,
    private toast: ToastService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    // The page follows the URL; switching between the two keeps this component (and edits)
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const section = params.get('section');
      if (section !== 'profile' && section !== 'security') {
        this.router.navigate(['/settings/profile'], { replaceUrl: true });
        return;
      }
      const switched = section !== this.activeSection;
      this.activeSection = section;
      if (this.loadState === 'ready' && switched && !this.isPhone()) {
        window.scrollTo({ top: 0 });
      }
    });
    this.load();
  }

  // Phones show both sections on one page, so a link to Security scrolls down to it
  private showSectionOnPhone(): void {
    if (this.activeSection === 'security' && this.isPhone()) {
      document.getElementById('security')?.scrollIntoView({ block: 'start' });
    }
  }

  private isPhone(): boolean {
    return window.matchMedia('(max-width: 640px)').matches;
  }

  load(): void {
    this.loadState = 'loading';
    this.userService.getMe().subscribe({
      next: (response) => {
        if (!response?.isSuccess) {
          this.loadState = 'error';
          return;
        }
        const me = response.result;
        this.userName = me.userName;
        this.currentEmail = me.email;
        this.pictureUrl = me.profilePictureUrl || DEFAULT_PICTURE;
        this.savedProfile = {
          firstName: me.firstName ?? '',
          lastName: me.lastName ?? '',
          bio: me.bio ?? '',
        };
        this.profileForm.reset(this.savedProfile);
        this.loadState = 'ready';
        setTimeout(() => this.showSectionOnPhone());
      },
      error: () => (this.loadState = 'error'),
    });
  }

  // ---------- Profile picture: saved as soon as it's chosen ----------

  togglePictureMenu(): void {
    this.pictureMenuOpen = !this.pictureMenuOpen;
    if (this.pictureMenuOpen) {
      // Into the menu, on the first picture, once it's rendered
      setTimeout(() =>
        this.pictureArea?.nativeElement.querySelector<HTMLButtonElement>('.settings__picture')?.focus(),
      );
    }
  }

  closePictureMenu(returnFocus = false): void {
    if (!this.pictureMenuOpen) return;
    this.pictureMenuOpen = false;
    if (returnFocus) this.pictureButton?.nativeElement.focus();
  }

  choosePreset(url: string): void {
    this.closePictureMenu(true);
    if (url === this.pictureUrl) return;

    this.pictureSaving = true;
    this.userActionsService.updateUser({ profilePictureUrl: url }).subscribe({
      next: () => this.pictureSaved(url),
      error: (err: HttpErrorResponse) => this.pictureFailed(err),
    });
  }

  async onPictureFileChosen(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // so choosing the same file again still fires
    this.closePictureMenu(true);
    if (!file) return;

    this.pictureSaving = true;
    try {
      const { blob, fileName } = await prepareProfilePicture(file);
      this.userActionsService.uploadProfilePicture(blob, fileName).subscribe({
        next: (response) => this.pictureSaved(response.result),
        error: (err: HttpErrorResponse) => this.pictureFailed(err),
      });
    } catch (err) {
      this.pictureSaving = false;
      this.toast.error(
        err instanceof PictureError ? err.message : "Your picture couldn't be uploaded. Please try again.",
        'image',
      );
    }
  }

  // An uploaded picture that's gone (or a broken URL) shows the default instead
  onPictureError(): void {
    this.pictureUrl = DEFAULT_PICTURE;
  }

  private pictureSaved(url: string): void {
    this.pictureSaving = false;
    this.pictureUrl = url;
    this.updateCurrentUser({ profilePictureUrl: url });
    this.toast.success('Profile picture updated', 'image');
  }

  private pictureFailed(err: HttpErrorResponse): void {
    this.pictureSaving = false;
    const messages: string[] = err?.error?.errorMessages ?? [];
    this.toast.error(messages[0] ?? "Your picture couldn't be saved. Please try again.", 'image');
  }

  // Click outside the menu, or Escape, closes it
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.pictureMenuOpen && !this.pictureArea?.nativeElement.contains(event.target as Node)) {
      this.closePictureMenu();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closePictureMenu(true);
  }

  // ---------- Profile: names and bio ----------

  saveProfile(): void {
    this.submitted.profile = true;
    if (this.profileForm.invalid) return;

    const { firstName, lastName, bio } = this.profileForm.getRawValue();
    const changes = {
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      bio: bio.trim(),
    };
    this.save('profile', changes, () => {
      this.savedProfile = changes;
      this.profileForm.reset(changes);
      this.forgetLeftFields(this.profileForm);
      this.updateCurrentUser(changes);
      this.toast.success('Profile saved', 'person-check');
    });
  }

  // ---------- Account & security: closed rows, one form open at a time ----------

  openRow: AccountRow | null = null;

  toggleRow(row: AccountRow): void {
    if (this.openRow === row) {
      this.closeRow(row);
      return;
    }
    // Opening one closes the other, without keeping what was typed there
    if (this.openRow) this.clearRow(this.openRow);
    this.openRow = row;
    // Into the form, on its first field, once it's rendered
    setTimeout(() => document.getElementById(FIRST_FIELD[row])?.focus());
  }

  // Cancel, or after saving: close, clear, and put focus back on the row's Change button
  closeRow(row: AccountRow): void {
    this.clearRow(row);
    this.openRow = null;
    setTimeout(() => document.getElementById(`${row}-change`)?.focus());
  }

  private clearRow(row: AccountRow): void {
    this.submitted[row] = false;
    this.apiErrors[row] = [];
    const form = row === 'email' ? this.emailForm : this.passwordForm;
    form.reset();
    this.forgetLeftFields(form);
    if (row === 'password') this.newPasswordFocused = false;
  }

  // ---------- Email ----------

  get emailUnchanged(): boolean {
    return (
      this.emailForm.controls.email.value.trim().toLowerCase() === this.currentEmail.toLowerCase()
    );
  }

  saveEmail(): void {
    this.submitted.email = true;
    if (this.emailForm.invalid || this.emailUnchanged) return;

    const email = this.emailForm.controls.email.value.trim();
    const currentPassword = this.emailForm.controls.currentPassword.value;
    this.save('email', { email, currentPassword }, () => {
      this.currentEmail = email;
      this.updateCurrentUser({ email });
      this.closeRow('email');
      this.toast.success('Email updated', 'envelope-check');
    });
  }

  // ---------- Password ----------

  newPasswordFocused = false;

  get newPassword(): string {
    return this.passwordForm.controls.newPassword.value;
  }

  // The rules pop up while typing; after leaving the field, the error names what's missing
  get missingPasswordRules(): string {
    return this.passwordRules
      .filter((rule) => !rule.test(this.newPassword))
      .map((rule) => rule.label.toLowerCase())
      .join(', ');
  }

  savePassword(): void {
    this.submitted.password = true;
    if (this.passwordForm.invalid) return;

    const { currentPassword, newPassword } = this.passwordForm.getRawValue();
    this.save('password', { password: newPassword, currentPassword }, () => {
      this.closeRow('password');
      this.toast.success('Password updated', 'shield-check');
    });
  }

  // ---------- Shared ----------

  // Fields the user has moved on from to another field
  private leftFields = new Set<AbstractControl>();

  // Only moving to another field counts. Moving to a button doesn't: an error appearing
  // then would push the button down between press and release, and the click would be lost.
  // The button either saves (which shows every error) or closes the form.
  leftField(control: AbstractControl, event: FocusEvent): void {
    const next = event.relatedTarget as HTMLElement | null;
    if (next?.matches('input, textarea, select')) this.leftFields.add(control);
  }

  // Mistakes (a wrong format, a mismatch) show once the user has moved on to another
  // field; "this is empty" only after a save attempt, so passing through doesn't nag
  showError(card: Card, control: AbstractControl): boolean {
    if (!control.invalid) return false;
    if (this.submitted[card]) return true;
    return this.leftFields.has(control) && !control.hasError('required');
  }

  hasLeft(control: AbstractControl): boolean {
    return this.leftFields.has(control);
  }

  private forgetLeftFields(form: { controls: Record<string, AbstractControl> }): void {
    Object.values(form.controls).forEach((c) => this.leftFields.delete(c));
  }

  // Profile card's Cancel: back to what was last saved
  cancelProfile(): void {
    this.submitted.profile = false;
    this.apiErrors.profile = [];
    this.profileForm.reset(this.savedProfile);
    this.forgetLeftFields(this.profileForm);
  }

  // A closed row has nothing typed in it (closing clears it)
  hasUnsavedChanges(): boolean {
    return this.profileForm.dirty || this.emailForm.dirty || this.passwordForm.dirty;
  }

  // Closing or reloading the tab; the browser shows its own "Leave site?" question
  @HostListener('window:beforeunload', ['$event'])
  warnBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  private save(card: Card, payload: object, onSuccess: () => void): void {
    this.saving[card] = true;
    this.apiErrors[card] = [];
    this.userActionsService.updateUser(payload).subscribe({
      next: () => {
        this.saving[card] = false;
        this.submitted[card] = false;
        onSuccess();
      },
      // Show the API's reason, e.g. a wrong current password or an email that's taken
      error: (err: HttpErrorResponse) => {
        this.saving[card] = false;
        const messages: string[] = err?.error?.errorMessages ?? [];
        this.apiErrors[card] = messages.length
          ? messages
          : ["Your changes couldn't be saved. Please try again."];
      },
    });
  }

  // Keeps the topbar avatar and the stored user in step with what was saved
  private updateCurrentUser(changes: Partial<ILoggedInUser>): void {
    this.authService
      .getCurrentUser()
      .pipe(take(1))
      .subscribe((user) => {
        if (user) this.authService.setCurrentUser({ ...user, ...changes });
      });
  }
}
