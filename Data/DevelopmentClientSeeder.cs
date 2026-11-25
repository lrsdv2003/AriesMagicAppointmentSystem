
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;

namespace AriesMagicAppointmentSystem.Data
{
    public static class DevelopmentClientSeeder
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

            var email = configuration["DevelopmentSeed:ClientEmail"]?.Trim();
            var password = configuration["DevelopmentSeed:ClientPassword"];
            var fullName = configuration["DevelopmentSeed:ClientFullName"];

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(fullName))
            {
                throw new InvalidOperationException(
                    "Development Client seed configuration is incomplete.");
            }

            var existingUser = await userManager.FindByEmailAsync(email);

            if (existingUser != null)
            {
                if (!await userManager.IsInRoleAsync(existingUser, "Client"))
                {
                    throw new InvalidOperationException(
                        "The configured development account already exists but is not a Client.");
                }

                Console.WriteLine("Development Client already exists. No changes made.");
                return;
            }

            var client = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(client, password);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Development Client creation failed: " +
                    string.Join(", ", result.Errors.Select(e => e.Code)));
            }

            var roleResult = await userManager.AddToRoleAsync(client, "Client");

            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(client);

                throw new InvalidOperationException(
                    "Development Client role assignment failed.");
            }

            Console.WriteLine("Development Client account created successfully.");
        }
    }
}
