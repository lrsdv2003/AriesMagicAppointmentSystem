using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;

namespace AriesMagicAppointmentSystem.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            string[] roles = { "Client", "Staff", "Admin" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }
            await EnsureUserAsync(
                userManager,
                email: "client@ariesmagic.com",
                password: "client123",
                fullName: "Default Client",
                role: "Client");

            await EnsureUserAsync(
                userManager,
                email: "admin@ariesmagic.com",
                password: "admin123",
                fullName: "System Admin",
                role: "Admin");

            await EnsureUserAsync(
                userManager,
                email: "johnmichaelps21@gmail.com",
                password: "staff123",
                fullName: "Staff User",
                role: "Staff");
        }

        private static async Task EnsureUserAsync(
            UserManager<ApplicationUser> userManager,
            string email,
            string password,
            string fullName,
            string role)
        {
            var user = await userManager.FindByEmailAsync(email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                {
                    throw new Exception(string.Join("; ", result.Errors.Select(e => e.Description)));
                }
            }

            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }
        }
    }
}