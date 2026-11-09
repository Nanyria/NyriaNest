using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NyriaNest.Application.Accounts;
using NyriaNest.Application.Loans;
using NyriaNest.Application.Mapping;
using NyriaNest.Application.ReadingList;
using NyriaNest.Application.Reviews;
using NyriaNest.Application.Services;
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

            services.AddAutoMapper(cfg =>
            {
                cfg.LicenseKey = configuration["AutoMapper:LicenseKey"];
            }, typeof(MappingConfig));

            return services;
        }
    }
}
