// Enums travel as numbers, so this must match the backend's BookStatusEnum order exactly
export enum BookStatusEnum {
    Available,
    Reserved,
    CheckedOut,
    Returned,
    Overdue,
    Added,
    Lost,     // set by an admin
    Removed,  // set by an admin
    Ready,    // e-book with an EPUB; like Overdue, worked out and never stored
    NoFile,   // e-book without an EPUB; worked out too
}

export const BookStatusDisplayNames: { [key in BookStatusEnum]: string } = {
    [BookStatusEnum.Available]: 'Available',
    [BookStatusEnum.Reserved]: 'Reserved',
    [BookStatusEnum.CheckedOut]: 'Checked out',
    [BookStatusEnum.Returned]: 'Returned',
    [BookStatusEnum.Overdue]: 'Overdue',
    [BookStatusEnum.Added]: 'Added',
    [BookStatusEnum.Lost]: 'Lost',
    [BookStatusEnum.Removed]: 'Removed',
    [BookStatusEnum.Ready]: 'Ready',
    [BookStatusEnum.NoFile]: 'No file',
};
export enum GenreEnums {
    None,
    Fantasy,
    Children,
    Romance,
    Horror,
    Thriller,
    Mystery,
    ScienceFiction,
    NonFiction,
    Fiction,
    Biography,
    FeelGood,
    Poetry,
    SelfHelp,
    All,
}



export const GenreDisplayNames: Record<GenreEnums, string> = {
    [GenreEnums.All]: 'All books',
    [GenreEnums.None]: 'Not set',
    [GenreEnums.Fantasy]: 'Fantasy',
    [GenreEnums.Children]: 'Children & Young Adult',
    [GenreEnums.Romance]: 'Romance',
    [GenreEnums.Horror]: 'Horror',
    [GenreEnums.Thriller]: 'Thriller',
    [GenreEnums.Mystery]: 'Mystery',
    [GenreEnums.ScienceFiction]: 'Science Fiction',
    [GenreEnums.NonFiction]: 'Non-fiction',
    [GenreEnums.Fiction]: 'Fiction',
    [GenreEnums.Biography]: 'Biography',
    [GenreEnums.FeelGood]: 'Feelgood',
    [GenreEnums.Poetry]: 'Poetry',
    [GenreEnums.SelfHelp]: 'Self-help',
};
export enum BookTypeEnums {
    Undefined,
    Paperback,
    Hardcover,
    EBook,
    Audiobook,
}

export const BookTypeDisplayNames: { [key in BookTypeEnums]: string } = {
    [BookTypeEnums.Undefined]: 'Unspecified',
    [BookTypeEnums.Paperback]: 'Paperback',
    [BookTypeEnums.Hardcover]: 'Hardcover',
    [BookTypeEnums.EBook]: 'E-book',
    [BookTypeEnums.Audiobook]: 'Audiobook',
}
export enum LanguageEnums {
    Undefined,
    Swedish,
    English
}
export const LanguageDisplayNames: { [key in LanguageEnums]: string } = {
    [LanguageEnums.Undefined]: 'Unspecified',
    [LanguageEnums.Swedish]: 'Swedish',
    [LanguageEnums.English]: 'English'
}   

// Travels as a number: must match the backend's NotificationType exactly.
// Never change or reuse a number, only add at the end. BookRemoved, BookLoanChangedByAdmin and
// BookStatusChangedByAdmin are sent so far.
export enum NotificationType {
    BookRemoved = 0,
    LoanRequested = 1,
    LoanRequestAnswered = 2,
    ReturnReminderDue = 3,
    LoanOverdue = 4,
    ReturnMarked = 5,
    ReturnConfirmed = 6,
    ReservationAvailable = 7,
    SharedLibraryRequestAnswered = 8,
    FriendRequestReceived = 9,
    FriendRequestAccepted = 10,
    FriendAddedBook = 11,
    BookLoanChangedByAdmin = 12,
    BookStatusChangedByAdmin = 13,
}

// Travels as a number: must match the backend's FriendshipStatus (#84).
export enum FriendshipStatus {
    Pending = 0,
    Accepted = 1,
    Declined = 2,
}

// Travels as a number: must match the backend's BorrowRequestStatus (#85).
// Only Requested is open; the others are finished.
export enum BorrowRequestStatus {
    Requested = 0,
    Lent = 1,       // accepted: a normal loan
    Declined = 2,
    Cancelled = 3,  // withdrawn by the friend, or cancelled by an admin
    Expired = 4,    // the owner didn't answer within a month
    Queued = 5,     // lent to another friend; this one became a reservation
}
