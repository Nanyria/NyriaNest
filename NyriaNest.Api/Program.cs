using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NyriaNest.Api.Auth;
using NyriaNest.Api.Controllers;
using NyriaNest.Application;
using NyriaNest.Domain.Entities;
using NyriaNest.Infrastructure;
using NyriaNest.Infrastructure.Data;
using System.Text;
using System.Threading.RateLimiting;

namespace NyriaNest.Api
{
    public class Program
    {
        // A token refused because the user's password or email changed (#113)
        private const string SessionEndedMessage = "The session has ended: the password or email has changed.";
        public const string SessionEndedHeader = "X-Session-Ended";

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.WriteIndented = true;
                });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter 'Bearer' [space] and then your valid JWT token."
                });

                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
            });

            // Each layer registers its own classes (see DependencyInjection.cs in each project)
            builder.Services.AddApplication(builder.Configuration);
            builder.Services.AddInfrastructure(builder.Configuration);

            // The app normally calls the API from its own origin (the dev proxy, or the
            // Angular build served from wwwroot), which needs no CORS. Only the origins
            // listed in Cors:AllowedOrigins may call it from another site.
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            builder.Services.AddCors(setup => setup.AddPolicy("default", options =>
            {
                options.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader()
                    .WithExposedHeaders(SessionEndedHeader); // readable by the app from another origin too
            }));

            builder.Services.AddIdentity<User, IdentityRole>(options =>
                {
                    // Spelled out because the frontend shows the same rules (register and Settings)
                    options.Password.RequiredLength = 8;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireDigit = true;
                    options.Password.RequireNonAlphanumeric = true;
                    // Also makes Identity check that the email is valid, on register and on update
                    options.User.RequireUniqueEmail = true;
                })
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

            // Password reset links (#51) work for an hour
            builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
                options.TokenLifespan = TimeSpan.FromHours(1));

            // Emailed links point to the frontend, so it must be known
            if (string.IsNullOrWhiteSpace(builder.Configuration["Frontend:BaseUrl"]))
                throw new InvalidOperationException("Frontend:BaseUrl is missing. Set it to the address the app is served from, e.g. https://nyrianest.example.");

            // Forgot/reset password: a few tries per IP per 15 minutes, then 429. Stops the
            // endpoints being used to flood someone's inbox or to guess reset tokens.
            var passwordResetLimit = builder.Configuration.GetValue("RateLimiting:PasswordResetPermitLimit", 5);
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(AuthController.PasswordResetRateLimit, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = passwordResetLimit,
                            Window = TimeSpan.FromMinutes(15),
                            QueueLimit = 0,
                        }));
            });

            var jwtKey = builder.Configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
                throw new InvalidOperationException("Jwt:Key is missing or shorter than 32 characters. Set it in User Secrets or app settings.");

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                };

                // A token is only good while its security stamp matches the user's. Identity
                // changes the stamp on every password and email change, so such a change
                // ends the user's other sessions at once (#113). Not cached: one lookup by
                // primary key per request, and an old token stops working immediately.
                // Tokens without a stamp (issued before #113) are refused: log in again.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var userId = context.Principal?.FindFirst(TokenService.UserIdClaim)?.Value;
                        var stamp = context.Principal?.FindFirst(TokenService.SecurityStampClaim)?.Value;
                        if (userId is null || stamp is null)
                        {
                            context.Fail("The token has no user or security stamp.");
                            return;
                        }

                        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<User>>();
                        var user = await userManager.FindByIdAsync(userId);
                        if (user is null || await userManager.GetSecurityStampAsync(user) != stamp)
                            context.Fail(SessionEndedMessage);
                    },
                    // Says why, so the app can tell the user their password or email was
                    // changed, instead of just "please log in again"
                    OnChallenge = context =>
                    {
                        if (context.AuthenticateFailure?.Message == SessionEndedMessage)
                            context.Response.Headers[SessionEndedHeader] = "credentials-changed";
                        return Task.CompletedTask;
                    },
                };
            });

            builder.Services.AddScoped<ITokenService, TokenService>();

            var app = builder.Build();

            app.Services.InitialiseDatabaseAsync().GetAwaiter().GetResult();

            // Swagger lists every endpoint and lets anyone try them, so only in Development
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseCors("default");

            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapControllers();
            app.MapFallback("/api/{**rest}", () => Results.NotFound());
            app.MapFallbackToFile("index.html");

            app.Run();
        }
    }
}