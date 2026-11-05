using NyriaNest.Api.Helpers.Enums;
using NyriaNest.Api.Models.Users;
using Microsoft.AspNetCore.Identity;

namespace NyriaNest.Api.Helpers
{
    public static class Helper
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
                user.Role = roles != null && roles.Count > 0 ? roles[0] : Roles.User;
                await userManager.UpdateAsync(user);
            }
        }
    }

}
