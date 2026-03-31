using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace AriesMagicAppointmentSystem.Data
{
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<AriesMagicAppointmentSystem.Data.ApplicationDbContext>
    {
        public AriesMagicAppointmentSystem.Data.ApplicationDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
            }

            var optionsBuilder = new DbContextOptionsBuilder<AriesMagicAppointmentSystem.Data.ApplicationDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return new AriesMagicAppointmentSystem.Data.ApplicationDbContext(optionsBuilder.Options);
        }
    }
}