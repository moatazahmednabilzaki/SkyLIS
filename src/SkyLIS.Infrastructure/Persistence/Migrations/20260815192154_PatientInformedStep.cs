using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyLIS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatientInformedStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "patient_informed_at_utc",
                schema: "visits",
                table: "samples",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "patient_informed_at_utc",
                schema: "visits",
                table: "samples");
        }
    }
}
