using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NyriaNest.Application.Interfaces;
using NyriaNest.Infrastructure.Data;
using NyriaNest.Infrastructure.Repositories;
using NyriaNest.Infrastructure.Services;
using NyriaNest.Infrastructure.Storage;

namespace NyriaNest.Infrastructure
{
    public static class DependencyInjection
    {
        /// <summary>
        /// The database, repositories, email and file storage behind Application's
        /// interfaces. Identity itself (AddIdentity) is set up in the Api, because it
        /// comes from ASP.NET; it uses AppDbContext as its store.
        /// </summary>
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("ConnectionToDB")));
            // The same AppDbContext instance as the repositories get, not a second one
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

            services.AddScoped<IBookRepo, BookRepo>();
            services.AddScoped<IUserRepo, UserRepo>();
            services.AddScoped<IEmailService, EmailService>();
            services.AddSingleton<IFileStorage, LocalFileStorage>();

            return services;
        }

        /// <summary>Brings the database up to date and seeds the roles and the admin account.</summary>
        public static async Task InitialiseDatabaseAsync(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // The migrations are written for SQL Server. Any other provider (the SQLite
            // database the tests use) gets its schema straight from the model instead.
            if (db.Database.IsSqlServer())
                await db.Database.MigrateAsync();
            else
                await db.Database.EnsureCreatedAsync();

            await DatabaseSeeder.SeedRolesAndAdminAsync(scope.ServiceProvider);
            await DatabaseSeeder.AssignUserRoleToUsersWithoutRoleAsync(scope.ServiceProvider);
        }
    }
}
