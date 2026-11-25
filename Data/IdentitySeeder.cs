
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;

namespace AriesMagicAppointmentSystem.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider
                .GetRequiredService<RoleManager<IdentityRole>>();

            var roles = ApplicationRoles.All;

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var result = await roleManager.CreateAsync(
                        new IdentityRole(role));

                    if (!result.Succeeded &&
                        !await roleManager.RoleExistsAsync(role))
                    {
                        throw new InvalidOperationException(
                            "Identity role initialization failed. No privileged account was created.");
                    }
                }
            }
        }
    }
}
