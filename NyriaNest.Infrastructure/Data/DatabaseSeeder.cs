using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Infrastructure.Data
{
    public static class DatabaseSeeder
    {
        public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seeding");

            var roles = Roles.AllRoles;
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            var superAdminEmail = config["SeedAdmin:Email"];
            var superAdminPassword = config["SeedAdmin:Password"];

            if (string.IsNullOrWhiteSpace(superAdminEmail) || string.IsNullOrWhiteSpace(superAdminPassword))
            {
                logger.LogWarning("SeedAdmin:Email or SeedAdmin:Password is not configured. Skipping SuperAdmin seeding.");
                return;
            }

            var superAdminUser = await userManager.FindByEmailAsync(superAdminEmail);
            if (superAdminUser == null)
            {
                superAdminUser = new User
                {
                    UserName = "Nanyria",
                    Email = superAdminEmail,
                    FirstName = "Nathalee",
                    LastName = "Wilund",
                    EmailConfirmed = true,
                    Role = Roles.SuperAdmin
                };
                var createResult = await userManager.CreateAsync(superAdminUser, superAdminPassword);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                    throw new Exception($"Failed to create SuperAdmin user: {errors}");
                }
                await userManager.AddToRoleAsync(superAdminUser, Roles.SuperAdmin);
            }
        }
        public static async Task AssignUserRoleToUsersWithoutRoleAsync(IServiceProvider serviceProvider)
        {
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
            var users = userManager.Users.ToList();

            foreach (var user in users)
            {
                var roles = await userManager.GetRolesAsync(user);
                if (roles == null || roles.Count == 0)
                {
                    await userManager.AddToRoleAsync(user, Roles.User);
                }

                // Only save when the Role column is out of date. This runs at every
                // startup, and saving gives the user a new ConcurrencyStamp (#60).
                var role = roles != null && roles.Count > 0 ? roles[0] : Roles.User;
                if (user.Role != role)
                {
                    user.Role = role;
                    await userManager.UpdateAsync(user);
                }
            }
        }

        // ---- Demo data (#83) ----

        private sealed record DemoBook(string Title, string Author, int Year, GenreEnums Genre, BookTypeEnums Type,
            LanguageEnums Language = LanguageEnums.English, string? LentTo = null);

        private sealed record DemoUser(string UserName, string FirstName, string LastName, DemoBook[] Books);

        // Each demo user's own shelf: physical books only (ADR 0004). One book is lent to a name.
        private static readonly DemoUser[] DemoUsers =
        [
            new("alma.reads", "Alma", "Berg",
            [
                new("The Hobbit", "J.R.R. Tolkien", 1937, GenreEnums.Fantasy, BookTypeEnums.Hardcover),
                new("Rebecca", "Daphne du Maurier", 1938, GenreEnums.Mystery, BookTypeEnums.Paperback),
                new("The Night Circus", "Erin Morgenstern", 2011, GenreEnums.Fantasy, BookTypeEnums.Paperback,
                    LentTo: "Ruth (neighbour)"),
            ]),
            new("boris.books", "Boris", "Lind",
            [
                new("Dune", "Frank Herbert", 1965, GenreEnums.ScienceFiction, BookTypeEnums.Paperback),
                new("The Name of the Wind", "Patrick Rothfuss", 2007, GenreEnums.Fantasy, BookTypeEnums.Hardcover),
            ]),
            new("cleo.shelf", "Cleo", "Hart",
            [
                new("Pippi Långstrump", "Astrid Lindgren", 1945, GenreEnums.Children, BookTypeEnums.Hardcover, LanguageEnums.Swedish),
                new("Anne of Green Gables", "L.M. Montgomery", 1908, GenreEnums.Children, BookTypeEnums.Paperback),
            ]),
        ];

        // The shared library: public-domain e-books, metadata only (no EPUB file yet).
        private static readonly DemoBook[] SharedEbooks =
        [
            new("Pride and Prejudice", "Jane Austen", 1813, GenreEnums.Romance, BookTypeEnums.EBook),
            new("Frankenstein", "Mary Shelley", 1818, GenreEnums.Horror, BookTypeEnums.EBook),
            new("Alice's Adventures in Wonderland", "Lewis Carroll", 1865, GenreEnums.Children, BookTypeEnums.EBook),
        ];

        /// <summary>
        /// Fills a development database with demo users who own physical books (one lent to a name)
        /// and a few shared-library e-books. Startup runs it only when SeedDemoData:Enabled is true.
        /// Safe to run again: users and shared e-books that already exist are skipped.
        /// </summary>
        public static async Task SeedDemoDataAsync(IServiceProvider serviceProvider)
        {
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
            var db = serviceProvider.GetRequiredService<AppDbContext>();
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seeding");

            var password = config["SeedDemoData:Password"];
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("SeedDemoData:Password is not configured. Skipping demo data.");
                return;
            }

            foreach (var demoUser in DemoUsers)
            {
                if (await userManager.FindByNameAsync(demoUser.UserName) != null)
                    continue;

                var user = new User
                {
                    UserName = demoUser.UserName,
                    Email = $"{demoUser.UserName}@example.com",
                    FirstName = demoUser.FirstName,
                    LastName = demoUser.LastName,
                    EmailConfirmed = true,
                    Role = Roles.User
                };
                var createResult = await userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                    throw new Exception($"Failed to create demo user {demoUser.UserName}: {errors}");
                }
                await userManager.AddToRoleAsync(user, Roles.User);

                foreach (var demoBook in demoUser.Books)
                    AddOwnBook(db, user, demoBook);

                await db.SaveChangesAsync();
                logger.LogInformation("Seeded demo user {UserName} with {Count} books.", demoUser.UserName, demoUser.Books.Length);
            }

            foreach (var demoBook in SharedEbooks)
            {
                if (await db.Books.AnyAsync(b => b.OwnerId == null && b.Title == demoBook.Title))
                    continue;

                var book = NewBook(demoBook, ownerId: null);
                db.Books.Add(book);
                db.StatusHistoryItems.Add(new StatusHistoryItem { Book = book, BookStatus = BookStatusEnum.Added, Notes = "Added by the demo seeder" });
            }
            await db.SaveChangesAsync();
        }

        private static void AddOwnBook(AppDbContext db, User owner, DemoBook demoBook)
        {
            var now = DateTime.UtcNow;
            var book = NewBook(demoBook, owner.Id);
            db.Books.Add(book);
            db.StatusHistoryItems.Add(new StatusHistoryItem
            {
                Book = book,
                UserId = owner.Id,
                BookStatus = BookStatusEnum.Added,
                Timestamp = now.AddDays(-30),
                Notes = "Added by the demo seeder"
            });

            if (demoBook.LentTo is null)
                return;

            // Lent two weeks ago, back in two weeks. Mirrors what a checkout sets on the book.
            var loan = new CheckedOutItem
            {
                Book = book,
                BorrowerName = demoBook.LentTo,
                CheckOutDate = now.AddDays(-14),
                ReturnDate = now.AddDays(14),
            };
            db.CheckOutItems.Add(loan);
            book.BookStatus = BookStatusEnum.CheckedOut;
            book.AvailabilityDate = loan.ReturnDate;
            db.StatusHistoryItems.Add(new StatusHistoryItem
            {
                Book = book,
                UserId = owner.Id,
                BookStatus = BookStatusEnum.CheckedOut,
                Timestamp = loan.CheckOutDate,
                Notes = $"Lent to {demoBook.LentTo}"
            });
        }

        private static Book NewBook(DemoBook demoBook, string? ownerId) => new()
        {
            Title = demoBook.Title,
            Author = demoBook.Author,
            PublicationYear = demoBook.Year,
            Genre = demoBook.Genre,
            BookType = demoBook.Type,
            Language = demoBook.Language,
            BookStatus = BookStatusEnum.Available,
            OwnerId = ownerId,
        };
    }

}
