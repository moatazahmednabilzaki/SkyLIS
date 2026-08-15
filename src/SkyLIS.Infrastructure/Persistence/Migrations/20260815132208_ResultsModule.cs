using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyLIS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResultsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "results");

            migrationBuilder.AddColumn<decimal>(
                name: "absurd_high",
                schema: "catalog",
                table: "lab_tests",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "absurd_low",
                schema: "catalog",
                table: "lab_tests",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "auto_verify",
                schema: "catalog",
                table: "lab_tests",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "critical_high",
                schema: "catalog",
                table: "lab_tests",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "critical_low",
                schema: "catalog",
                table: "lab_tests",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "delta_threshold_percent",
                schema: "catalog",
                table: "lab_tests",
                type: "numeric(7,2)",
                precision: 7,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ref_high",
                schema: "catalog",
                table: "lab_tests",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ref_low",
                schema: "catalog",
                table: "lab_tests",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_unit",
                schema: "catalog",
                table: "lab_tests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "test_results",
                schema: "results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    flag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delta_flagged = table.Column<bool>(type: "boolean", nullable: false),
                    previous_value = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    technically_validated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    technically_validated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    medically_validated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    medically_validated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    interpretive_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    signature_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    rerun_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "critical_notifications",
                schema: "results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    flagged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    called_person = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    called_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    read_back_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_critical_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_critical_notifications_test_results_test_result_id",
                        column: x => x.test_result_id,
                        principalSchema: "results",
                        principalTable: "test_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_critical_notifications_tenant_id_state",
                schema: "results",
                table: "critical_notifications",
                columns: new[] { "tenant_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_critical_notifications_test_result_id",
                schema: "results",
                table: "critical_notifications",
                column: "test_result_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_test_results_tenant_id_patient_id_test_code",
                schema: "results",
                table: "test_results",
                columns: new[] { "tenant_id", "patient_id", "test_code" });

            migrationBuilder.CreateIndex(
                name: "ix_test_results_tenant_id_status",
                schema: "results",
                table: "test_results",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_test_results_tenant_id_visit_test_id",
                schema: "results",
                table: "test_results",
                columns: new[] { "tenant_id", "visit_test_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "critical_notifications",
                schema: "results");

            migrationBuilder.DropTable(
                name: "test_results",
                schema: "results");

            migrationBuilder.DropColumn(
                name: "absurd_high",
                schema: "catalog",
                table: "lab_tests");

            migrationBuilder.DropColumn(
                name: "absurd_low",
                schema: "catalog",
                table: "lab_tests");

            migrationBuilder.DropColumn(
                name: "auto_verify",
                schema: "catalog",
                table: "lab_tests");

            migrationBuilder.DropColumn(
                name: "critical_high",
                schema: "catalog",
                table: "lab_tests");

            migrationBuilder.DropColumn(
                name: "critical_low",
                schema: "catalog",
                table: "lab_tests");

            migrationBuilder.DropColumn(
                name: "delta_threshold_percent",
                schema: "catalog",
                table: "lab_tests");

            migrationBuilder.DropColumn(
                name: "ref_high",
                schema: "catalog",
                table: "lab_tests");

            migrationBuilder.DropColumn(
                name: "ref_low",
                schema: "catalog",
                table: "lab_tests");

            migrationBuilder.DropColumn(
                name: "result_unit",
                schema: "catalog",
                table: "lab_tests");
        }
    }
}
