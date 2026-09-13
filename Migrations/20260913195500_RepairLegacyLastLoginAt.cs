using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    public partial class RepairLegacyLastLoginAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.AspNetUsers', 'LastLoginAt') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[AspNetUsers] ADD [LastLoginAt] datetime2 NULL;
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Compatibility migration: intentionally left non-destructive.
        }
    }
}
