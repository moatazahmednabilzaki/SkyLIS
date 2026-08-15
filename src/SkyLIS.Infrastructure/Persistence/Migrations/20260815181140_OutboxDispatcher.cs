using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyLIS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxDispatcher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_consumptions",
                schema: "outbox",
                columns: table => new
                {
                    handler_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_consumptions", x => new { x.handler_name, x.event_id });
                });

            migrationBuilder.CreateTable(
                name: "usage_meters",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    finalized_reports = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_meters", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usage_meters_tenant_id_year_month",
                schema: "platform",
                table: "usage_meters",
                columns: new[] { "tenant_id", "year", "month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_consumptions",
                schema: "outbox");

            migrationBuilder.DropTable(
                name: "usage_meters",
                schema: "platform");
        }
    }
}
