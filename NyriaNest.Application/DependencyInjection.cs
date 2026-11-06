using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NyriaNest.Application.Mapping;
using NyriaNest.Application.Services;

namespace NyriaNest.Application
{
    public static class DependencyInjection
    {
        /// <summary>The services with the business rules, and the AutoMapper profile.</summary>
        public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IBookService, BookService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IUserActionsService, UserActionsService>();
            services.AddScoped<IEpubService, EpubService>();

            services.AddAutoMapper(cfg =>
            {
                cfg.LicenseKey = configuration["AutoMapper:LicenseKey"];
            }, typeof(MappingConfig));

            return services;
        }
    }
}
