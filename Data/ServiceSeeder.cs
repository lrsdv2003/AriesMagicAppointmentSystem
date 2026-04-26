using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Data
{
    public static class ServiceSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context)
        {
            await EnsurePackageAsync(
                context,
                "Premium Package",
                19800,
                3,
                "Upgraded magic entertainment package with illusion magic, audience interaction, comedy, and live animal show."
            );

            await EnsurePackageAsync(
                context,
                "Deluxe Package",
                20000,
                3,
                "Entertainment package with bubble show, magician performance, and interactive party experience."
            );

            await EnsurePackageAsync(
                context,
                "All In Package",
                24500,
                3,
                "Complete premium package combining illusion magic and bubble show for a full event experience."
            );

            await context.SaveChangesAsync();
        }

        private static async Task EnsurePackageAsync(
            ApplicationDbContext context,
            string name,
            decimal price,
            int durationInHours,
            string description)
        {
            var existing = await context.Services.FirstOrDefaultAsync(s => s.Name == name);

            if (existing == null)
            {
                context.Services.Add(new Service
                {
                    Name = name,
                    Price = price,
                    DurationInHours = durationInHours,
                    Description = description,
                    IsArchived = false
                });
            }
        }
    }
}