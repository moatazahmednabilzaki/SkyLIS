using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyLIS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResultAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "amended_at_utc",
                schema: "results",
                table: "test_results",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "amended_by_user_id",
                schema: "results",
                table: "test_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "amendment_reason",
                schema: "results",
                table: "test_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_amended",
                schema: "results",
                table: "test_results",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "value_before_amendment",
                schema: "results",
                table: "test_results",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "amended_at_utc",
                schema: "results",
                table: "test_results");

            migrationBuilder.DropColumn(
                name: "amended_by_user_id",
                schema: "results",
                table: "test_results");

            migrationBuilder.DropColumn(
                name: "amendment_reason",
                schema: "results",
                table: "test_results");

            migrationBuilder.DropColumn(
                name: "is_amended",
                schema: "results",
                table: "test_results");

            migrationBuilder.DropColumn(
                name: "value_before_amendment",
                schema: "results",
                table: "test_results");
        }
    }
}
