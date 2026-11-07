
import {
  GenreEnums,
  BookStatusEnum,
  BookTypeEnums,
  LanguageEnums
} from '../Helpers/Enums/enum';

// Book interfaces
export interface Book {
  bookId: string;
  title: string;
  author: string;
  genre: GenreEnums;
  language: LanguageEnums;  
  publicationYear: string;
  bookDescription?: string;
  coverImagePath?: string;
  hasEpub: boolean;
  // Null for files uploaded before the name and size were stored
  epubFileName?: string | null;
  epubFileSize?: number | null;
  bookStatus: BookStatusEnum;
  bookType: BookTypeEnums;
  availabilityDate: Date;
  dateAdded: string; // ISO string (DateTime in C#)

  reviews?: string[];
  statusHistory: StatusHistoryItem[];
  reservations: ReservationItem[];
  checkedOutBy?: CheckedOutItem;
}

//Delete later, not used in the project
export interface UpdateBookDto {
  bookId: string;
  title: string;
  author: string;
  genre: GenreEnums;
  language: LanguageEnums;  
  publicationYear: string;
  bookDescription?: string;
  coverImagePath?: string;
  bookStatus: BookStatusEnum;
  bookType: BookTypeEnums;
}
export interface SlimBookDto {
  title: string;
  author: string;
  genre: GenreEnums;
  publicationYear: string;
  bookDescription?: string;
  bookType?: BookTypeEnums;
  language: LanguageEnums;
  coverImagePath?: string;
  // availabilityDate?: Date;

}

// What the edit book page loads (GET api/Book/{id}): no loans, reservations or reviews
export interface EditBookDto {
  bookId: string;
  title: string;
  author: string;
  bookDescription?: string;
  publicationYear: string;
  coverImagePath?: string;
  genre: GenreEnums;
  bookType: BookTypeEnums;
  language: LanguageEnums;
  hasEpub: boolean;
  epubFileName?: string | null;
  epubFileSize?: number | null;
}

export interface LibraryBook {
  bookId: string;
  title: string;
  author: string;
  genre: GenreEnums;
  language: LanguageEnums;
  publicationYear: string;
  bookDescription?: string;
  coverImagePath?: string;
  hasEpub: boolean;
  bookStatus: BookStatusEnum;
  bookType: BookTypeEnums;
  availabilityDate: string;
  dateAdded: string;

  reviewCount: number;
  averageRating: number | null;
  reservationCount: number;

  isCheckedOutByMe: boolean;
  isReservedByMe: boolean;
  myReservationAvailableDate: string | null;
}

// The API wraps every result like this
export interface ApiResponse<T> {
  isSuccess: boolean;
  statusCode: number;
  result: T;
  errorMessages: string[];
}

// One page of results plus the total, for "1–10 of 42" and the page buttons
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// A row in the admin catalogue (GET /api/Book/catalogue). Admin-only: includes borrower names.
export interface AdminBook {
  bookId: number;
  title: string;
  author: string;
  coverImagePath?: string | null;
  genre: GenreEnums;
  language: LanguageEnums;
  bookType: BookTypeEnums;
  publicationYear: number;
  hasEpub: boolean;
  bookStatus: BookStatusEnum;
  isOverdue: boolean;
  borrowerName?: string | null;
  dueDate?: string | null;
  nextInLineName?: string | null;
  expectedDate?: string | null; // when the book is expected to reach them (an estimate, not a hold)
  adminStatusNotes?: string | null;
}

// Must match the backend's CatalogueSort names. The API matches names in any case.
export type CatalogueSort = 'titleAsc' | 'titleDesc' | 'authorAsc' | 'yearDesc' | 'yearAsc';

// The statuses you can filter by: BookStatusEnum names, minus Returned and Added
// (the API rejects those). Sent as names, so the URL stays readable.
export type CatalogueStatus = 'available' | 'checkedOut' | 'overdue' | 'reserved' | 'lost' | 'removed';

export interface CatalogueQuery {
  q?: string;
  genre?: GenreEnums;
  bookType?: BookTypeEnums;
  status?: CatalogueStatus;
  needsAttention?: boolean;
  sort?: CatalogueSort;
  page?: number;
  pageSize?: number;
}
// User interfaces
export interface UserDto {
    userId: string;
    userName: string;
    firstName: string;
    lastName: string;
    email: string;
    password: string;
    bio: string;
    role: string; // 'User', 'Admin', 'SuperAdmin'
    profilePictureUrl?: string;
    checkedOutBooks: CheckedOutItem[];
    reservedBooks: ReservationItem[];
    userHistory: StatusHistoryItem[];
    readList: FavoriteItem[];
    adminRole?: boolean;
    isSuperAdmin: boolean;
}
export interface UpdateUserDto{
  firstName?: string;
  lastName?: string;
  email?: string;
  password?: string;
  profilePictureUrl?: string;
  bio?: string;
}
export interface CreateUserDto {
  userName: string;
  firstName: string;
  lastName: string;
  email: string;
  password: string;
}
export interface ILoggedInUser {
  id?: string;
  userId: string;
  userName: string;
  firstName: string;
  lastName: string;
  email: string;
  password: string;

  profilePictureUrl?: string;
  bio: string;
  role: string; // 'User', 'Admin', 'SuperAdmin'
  readList: FavoriteItem[];
  reviews: ReviewItemDto[];
  checkedOutBooks: CheckedOutItem[];
  reservedBooks: ReservationItem[];
  userHistory: StatusHistoryItem[];
  notifications: NotificationItem[];
  adminRole?: boolean;
  isSuperAdmin: boolean;
}

export interface UserHistory {
  userHistoryId: string;
  userId: string;
  user: ILoggedInUser;
  bookId: string;
  book: Book;
  action: BookStatusEnum;
  timestamp: Date;
  notes?: string;
}
export interface NotificationItem {
  notificationId: string;
  userId: string;
  message: string;
  isRead: boolean;
  createdAt: Date;
}
export interface CreateRatingItemDto {
  rating: number;
  createdAt?: string;
}
export interface CreateReviewItemDto {
  userId: string;
  bookId: number;
  reviewHeader?: string;
  reviewText?: string;
  ratingItem?: CreateRatingItemDto;
  createdAt?: string; // ISO string, will default to now if not provided
}

//Joint interfaces
export interface StatusHistoryItem {
  statusHistoryItemId: string;
  bookId: string;
  userId?: string;
  bookStatus: BookStatusEnum;
  timestamp?: Date;
  notes?: string;
}

export interface ReservationItem {
  id: string;
  userId: string;
  bookId: string;
  reservationDate: Date;
  availabilityDate?: Date;
  bookIsAvailableEmailSent?: Date;
}

export interface CheckedOutItem {
  id: string;
  userId: string;
  bookId: string;
  checkOutDate: Date;
  reminderEmailSent?: Date;
}

export interface FavoriteItem {
  userId: string;
  bookId: number;
  book: SlimBookDto;
  createdAt: string; // ISO string (DateTime in C#)
}
export interface ReviewItemDto {
  id: string;
  userId: string;
  bookId: string;
  BookDto: Book;
  reviewHeader: string;
  reviewText: string;
  ratingItem: RatingItemDto;
  createdAt: Date;
}

export interface RatingItemDto {
  id: string;
  reviewItemId: string;
  rating: number;
  createdAt: Date;
}

export interface INavlink {
  name: string;
  route: string;
}
