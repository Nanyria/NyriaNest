using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Infrastructure.Data
{
    public class AppDbContext : IdentityDbContext<User>, IAppDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {

        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            Database.BeginTransactionAsync(cancellationToken);

        public DbSet<Book> Books { get; set; }
        public DbSet<ReviewItem> ReviewItems { get; set; }
        public DbSet<RatingItem> RatingItems { get; set; }
        public DbSet<FavoriteItem> FavoriteItems { get; set; }
        public DbSet<StatusHistoryItem> StatusHistoryItems { get; set; }
        public DbSet<CheckedOutItem> CheckOutItems { get; set; }
        public DbSet<ReservationItem> ReservationItems { get; set; }
        public DbSet<NotificationItem> NotificationItems { get; set; }

        private const string IdentitySchema = "identity";
        private const string LibrarySchema = "library";

        // #59: login data goes in "identity", library data in "library". Every new table needs a line
        // here (SchemaTests fails otherwise). The migrations history table stays in dbo.
        // Raw SQL must name the schema, and IDENTITY is a T-SQL keyword: write [identity].[AspNetUsers].
        private static void MapSchemas(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable("AspNetUsers", IdentitySchema);
            modelBuilder.Entity<IdentityRole>().ToTable("AspNetRoles", IdentitySchema);
            modelBuilder.Entity<IdentityUserRole<string>>().ToTable("AspNetUserRoles", IdentitySchema);
            modelBuilder.Entity<IdentityUserClaim<string>>().ToTable("AspNetUserClaims", IdentitySchema);
            modelBuilder.Entity<IdentityUserLogin<string>>().ToTable("AspNetUserLogins", IdentitySchema);
            modelBuilder.Entity<IdentityUserToken<string>>().ToTable("AspNetUserTokens", IdentitySchema);
            modelBuilder.Entity<IdentityRoleClaim<string>>().ToTable("AspNetRoleClaims", IdentitySchema);

            modelBuilder.Entity<Book>().ToTable("Books", LibrarySchema);
            modelBuilder.Entity<CheckedOutItem>().ToTable("CheckOutItems", LibrarySchema);
            modelBuilder.Entity<ReservationItem>().ToTable("ReservationItems", LibrarySchema);
            modelBuilder.Entity<StatusHistoryItem>().ToTable("StatusHistoryItems", LibrarySchema);
            modelBuilder.Entity<ReviewItem>().ToTable("ReviewItems", LibrarySchema);
            modelBuilder.Entity<RatingItem>().ToTable("RatingItems", LibrarySchema);
            modelBuilder.Entity<FavoriteItem>().ToTable("FavoriteItems", LibrarySchema);
            modelBuilder.Entity<NotificationItem>().ToTable("NotificationItems", LibrarySchema);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            MapSchemas(modelBuilder);

            // ReviewItem: User (many reviews per user)
            modelBuilder.Entity<ReviewItem>()
                .HasOne(r => r.User)
                .WithMany(u => u.Reviews)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ReviewItem: Book (many reviews per book)
            modelBuilder.Entity<ReviewItem>()
                .HasOne(r => r.Book)
                .WithMany(b => b.Reviews) // <-- Reference the Reviews collection
                .HasForeignKey(r => r.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            // RatingItem: ReviewItem (1:1)
            modelBuilder.Entity<RatingItem>()
                .HasOne(r => r.ReviewItem)
                .WithOne(r => r.RatingItem)
                .HasForeignKey<RatingItem>(r => r.ReviewItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // FavoriteItem: User (many favorites per user)
            modelBuilder.Entity<FavoriteItem>()
                .HasOne(f => f.User)
                .WithMany(u => u.ReadList)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // FavoriteItem: Book (many favorites per book, but Book does not need navigation)
            modelBuilder.Entity<FavoriteItem>()
                .HasOne(f => f.Book)
                .WithMany()
                .HasForeignKey(f => f.BookId)
                .OnDelete(DeleteBehavior.Cascade);
            // ReservationItem relationships
            modelBuilder.Entity<ReservationItem>()
                .HasOne(r => r.User)
                .WithMany(u => u.ReservedBooks)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

            modelBuilder.Entity<ReservationItem>()
                .HasOne(r => r.Book)
                .WithMany(b => b.Reservations)
                .HasForeignKey(r => r.BookId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

            // CheckedOutItem relationships
            modelBuilder.Entity<CheckedOutItem>()
                .HasOne(c => c.User)
                .WithMany(u => u.CheckedOutBooks)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

            modelBuilder.Entity<CheckedOutItem>()
                .HasOne(c => c.Book)
                .WithOne(b => b.CheckedOutBy)
                .HasForeignKey<CheckedOutItem>(c => c.BookId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

            // StatusHistoryItem relationships
            modelBuilder.Entity<StatusHistoryItem>()
                .HasOne(sh => sh.Book)
                .WithMany(b => b.StatusHistory)
                .HasForeignKey(sh => sh.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StatusHistoryItem>()
                .HasOne(sh => sh.User)
                .WithMany(u => u.UserHistory)
                .HasForeignKey(sh => sh.UserId)
                .OnDelete(DeleteBehavior.Restrict);


            modelBuilder.Entity<NotificationItem>()
                .HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a book keeps its notifications and just clears the link (#91)
            modelBuilder.Entity<NotificationItem>()
                .HasOne(n => n.Book)
                .WithMany()
                .HasForeignKey(n => n.BookId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Book>()
                .Property(b => b.BookId)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn(seed: 1001, increment: 1);

            // Set a default value for Language (so existing rows/DB will get Undefined (0) if none provided)
            modelBuilder.Entity<Book>()
                .Property(b => b.Language)
                .HasDefaultValue(LanguageEnums.Undefined);

            modelBuilder.Entity<Book>()
                .Property(b => b.DateAdded)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<CheckedOutItem>()
                .Property(c => c.Id)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn(seed: 1001, increment: 1);

            modelBuilder.Entity<ReservationItem>()
                .Property(r => r.Id)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn(seed: 1001, increment: 1);

            modelBuilder.Entity<StatusHistoryItem>()
                .Property(sh => sh.StatusHistoryItemId)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn(seed: 1001, increment: 1);

            modelBuilder.Entity<ReviewItem>()
                .Property(r => r.Id)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn(seed: 1001, increment: 1);

            modelBuilder.Entity<RatingItem>()
                .Property(r => r.Id)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn(seed: 1001, increment: 1);

            modelBuilder.Entity<FavoriteItem>()
                .Property(r => r.Id)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn(seed: 1001, increment: 1);


            modelBuilder.Entity<Book>().HasData(
                new Book
                {
                    BookId = 1001,
                    Title = "The Great Gatsby",
                    Author = "F. Scott Fitzgerald",
                    Genre = GenreEnums.Fiction,
                    PublicationYear = 1925,
                    BookDescription = "Lorem Ipsum",
                    BookType = BookTypeEnums.Hardcover,
                    BookStatus = BookStatusEnum.Available,
                    Language = LanguageEnums.English,
                    DateAdded = new DateTime(2025, 5, 15, 0, 0, 0, DateTimeKind.Utc)
                },
                new Book
                {
                    BookId = 1002,
                    Title = "To Kill a Mockingbird",
                    Author = "Harper Lee",
                    Genre = GenreEnums.Fiction,
                    PublicationYear = 1960,
                    BookDescription = "Lorem Ipsum",
                    BookType = BookTypeEnums.Hardcover,
                    BookStatus = BookStatusEnum.Available,
                    Language = LanguageEnums.English,
                    DateAdded = new DateTime(2025, 5, 15, 0, 0, 0, DateTimeKind.Utc)
                },
                new Book
                {
                    BookId = 1003,
                    Title = "1984",
                    Author = "George Orwell",
                    Genre = GenreEnums.Fiction,
                    PublicationYear = 1949,
                    BookDescription = "Lorem Ipsum",
                    BookType = BookTypeEnums.Paperback,
                    BookStatus = BookStatusEnum.Available,
                    Language = LanguageEnums.English,
                    DateAdded = new DateTime(2025, 5, 15, 0, 0, 0, DateTimeKind.Utc)
                },
                new Book
                {
                    BookId = 1004,
                    Title = "Howl's Moving Castle",
                    Author = "Diana Wynne Jones",
                    Genre = GenreEnums.Fantasy,
                    PublicationYear = 1986,
                    BookDescription = "Sophie is turned into an old woman by a witch's curse and seeks help from the wizard Howl in his moving castle.",
                    BookType = BookTypeEnums.EBook,
                    BookStatus = BookStatusEnum.Available,
                    Language = LanguageEnums.English,
                    DateAdded = new DateTime(2025, 5, 15, 0, 0, 0, DateTimeKind.Utc)
                }

            );

            modelBuilder.Entity<StatusHistoryItem>().HasData(
                new StatusHistoryItem
                {
                    StatusHistoryItemId = 1001,
                    BookId = 1001,
                    BookStatus = BookStatusEnum.Added,
                    Timestamp = new DateTime(2025, 5, 14, 0, 0, 0, DateTimeKind.Utc),   // was DateTime.UtcNow.AddDays(-1)
                    Notes = "Initial added status"
                },
                new StatusHistoryItem
                {
                    StatusHistoryItemId = 1002,
                    BookId = 1002,
                    BookStatus = BookStatusEnum.Added,
                    Timestamp = new DateTime(2025, 5, 13, 0, 0, 0, DateTimeKind.Utc),   // was DateTime.UtcNow.AddDays(-2)
                    Notes = "Initial added status"
                },
                new StatusHistoryItem
                {
                    StatusHistoryItemId = 1003,
                    BookId = 1003,
                    BookStatus = BookStatusEnum.Added,
                    Timestamp = new DateTime(2025, 5, 12, 0, 0, 0, DateTimeKind.Utc),   // was DateTime.UtcNow.AddDays(-3)
                    Notes = "Initial added status"
                },
                new StatusHistoryItem
                {
                    StatusHistoryItemId = 1004,
                    BookId = 1004,
                    BookStatus = BookStatusEnum.Added,
                    Timestamp = new DateTime(2025, 5, 11, 0, 0, 0, DateTimeKind.Utc),
                    Notes = "Initial added status"
                }
            );


        }
    }
}