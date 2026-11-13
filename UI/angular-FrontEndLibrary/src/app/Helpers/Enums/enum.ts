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
// Never change or reuse a number, only add at the end. Only BookRemoved is sent so far.
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
}
