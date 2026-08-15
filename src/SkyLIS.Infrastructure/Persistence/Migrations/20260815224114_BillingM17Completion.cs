using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyLIS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BillingM17Completion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_refund",
                schema: "billing",
                table: "payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                schema: "billing",
                table: "payments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "credited_amount",
                schema: "billing",
                table: "invoices",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                schema: "billing",
                table: "invoices",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "discount_reason",
                schema: "billing",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cashier_shifts",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    opening_float = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    declared_cash = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    expected_cash = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    variance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashier_shifts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_notes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_notes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cashier_shifts_tenant_id_branch_id_status",
                schema: "billing",
                table: "cashier_shifts",
                columns: new[] { "tenant_id", "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id_credit_note_number",
                schema: "billing",
                table: "credit_notes",
                columns: new[] { "tenant_id", "credit_note_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id_invoice_id",
                schema: "billing",
                table: "credit_notes",
                columns: new[] { "tenant_id", "invoice_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cashier_shifts",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "credit_notes",
                schema: "billing");

            migrationBuilder.DropColumn(
                name: "is_refund",
                schema: "billing",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "reason",
                schema: "billing",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "credited_amount",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "discount_reason",
                schema: "billing",
                table: "invoices");
        }
    }
}
