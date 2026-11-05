using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NyriaNest.Api.Data;
using NyriaNest.Api.Helpers;
using NyriaNest.Api.Models.Users;
using NyriaNest.Api.Repositories;
using NyriaNest.Api.Services;
using NyriaNest.Api.Storage;
using System.Text;

namespace NyriaNest.Api
{
    public class Program
    {
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

            builder.Services.AddScoped<IBookService, BookService>();
            builder.Services.AddScoped<IUserService, UserService>();
            builder.Services.AddScoped<IUserActionsService, UserActionsService>();
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<IUserRepo, UserRepo>();
            builder.Services.AddScoped<IBookRepo, BookRepo>();
            builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
            builder.Services.AddScoped<IEpubService, EpubService>();

            builder.Services.AddAutoMapper(cfg =>
            {
                cfg.LicenseKey = builder.Configuration["AutoMapper:LicenseKey"];
            }, typeof(MappingConfig));

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("ConnectionToDB")));

            builder.Services.AddCors(setup => setup.AddPolicy("default", options =>
            {
                options.AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin();
            }));

            builder.Services.AddIdentity<User, IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

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
            });

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // The migrations are written for SQL Server. Any other provider (the SQLite
                // database the tests use) gets its schema straight from the model instead.
                if (db.Database.IsSqlServer())
                    db.Database.Migrate(); // Ensures DB is created and up-to-date
                else
                    db.Database.EnsureCreated();

                var services = scope.ServiceProvider;
                Helper.SeedRolesAndAdminAsync(services).GetAwaiter().GetResult();
                Helper.AssignUserRoleToUsersWithoutRoleAsync(services).GetAwaiter().GetResult();
            }

            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseCors("default");

            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapControllers();
            app.MapFallback("/api/{**rest}", () => Results.NotFound());
            app.MapFallbackToFile("index.html");

            app.Run();
        }
    }
}