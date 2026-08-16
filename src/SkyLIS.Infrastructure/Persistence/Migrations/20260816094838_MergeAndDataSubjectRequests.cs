using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyLIS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeAndDataSubjectRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_erased",
                schema: "patients",
                table: "patients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_patient_id",
                schema: "patients",
                table: "patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "data_subject_requests",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_subject_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_subject_requests_tenant_id_patient_id",
                schema: "patients",
                table: "data_subject_requests",
                columns: new[] { "tenant_id", "patient_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_subject_requests",
                schema: "patients");

            migrationBuilder.DropColumn(
                name: "is_erased",
                schema: "patients",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "merged_into_patient_id",
                schema: "patients",
                table: "patients");
        }
    }
}
