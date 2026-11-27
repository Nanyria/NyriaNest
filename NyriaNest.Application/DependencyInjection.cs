using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NyriaNest.Application.Accounts;
using NyriaNest.Application.Books;
using NyriaNest.Application.Common;
using NyriaNest.Application.Friends;
using NyriaNest.Application.Loans;
using NyriaNest.Application.Mapping;
using NyriaNest.Application.Notifications;
using NyriaNest.Application.ReadingList;
using NyriaNest.Application.Reviews;
using NyriaNest.Application.Users;

namespace NyriaNest.Application
{
    public static class DependencyInjection
    {
        /// <summary>The services with the business rules, and the AutoMapper profile.</summary>
        public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IBookService, BookService>();
            services.AddScoped<IUserAdminService, UserAdminService>();
            services.AddScoped<IAccountService, AccountService>();
            services.AddScoped<IEpubService, EpubService>();
            services.AddScoped<IProfilePictureService, ProfilePictureService>();
            services.AddScoped<IPasswordResetService, PasswordResetService>();
            services.AddScoped<IReadingListService, ReadingListService>();
            services.AddScoped<IReviewService, ReviewService>();
            services.AddScoped<IAvailabilityService, AvailabilityService>();
            services.AddScoped<IReservationService, ReservationService>();
            services.AddScoped<ILoanService, LoanService>();
            services.AddScoped<IBorrowRequestService, BorrowRequestService>();
            // #85: once-a-day work; DailyJobsService in the API runs every IDailyJob
            services.AddScoped<IDailyJob, LendingJob>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IFriendshipService, FriendshipService>();

            // #84: the 100 / 10 sharing limits (ADR 0005); the class holds the defaults
            services.Configure<SharingLimitsOptions>(configuration.GetSection(SharingLimitsOptions.SectionName));

            // #85: reminder and expiry timings; the class holds the defaults
            services.Configure<LendingOptions>(configuration.GetSection(LendingOptions.SectionName));

            // #85: new code asks this for the time, so tests can use a fake clock
            services.AddSingleton(TimeProvider.System);

            services.AddAutoMapper(cfg =>
            {
                cfg.LicenseKey = configuration["AutoMapper:LicenseKey"];
            }, typeof(MappingConfig));

            return services;
        }
    }
}
