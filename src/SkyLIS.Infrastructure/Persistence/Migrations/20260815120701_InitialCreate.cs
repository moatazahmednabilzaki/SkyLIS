using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyLIS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.EnsureSchema(
                name: "outbox");

            migrationBuilder.EnsureSchema(
                name: "patients");

            migrationBuilder.EnsureSchema(
                name: "visits");

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lab_tests",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    department = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required_condition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_tests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_series",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patients",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sex = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    national_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_confidential = table.Column<bool>(type: "boolean", nullable: false),
                    registered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_visit_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sample_types",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    container_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subdomain = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    plan_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    isolation_tier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    suspension_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "visits",
                schema: "visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_stat = table.Column<bool>(type: "boolean", nullable: false),
                    stat_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    registered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sample_conditions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    delay_minutes = table.Column<int>(type: "integer", nullable: true),
                    compatibility_group = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_conditions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sample_conditions_sample_types_sample_type_id",
                        column: x => x.sample_type_id,
                        principalSchema: "catalog",
                        principalTable: "sample_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "samples",
                schema: "visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barcode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sample_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condition_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    condition_delayed = table.Column<bool>(type: "boolean", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    condition_ready_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    collected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_samples", x => x.id);
                    table.ForeignKey(
                        name: "fk_samples_visits_visit_id",
                        column: x => x.visit_id,
                        principalSchema: "visits",
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_tests",
                schema: "visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_tests", x => x.id);
                    table.ForeignKey(
                        name: "fk_visit_tests_visits_visit_id",
                        column: x => x.visit_id,
                        principalSchema: "visits",
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_invoice_number",
                schema: "billing",
                table: "invoices",
                columns: new[] { "tenant_id", "invoice_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_visit_id",
                schema: "billing",
                table: "invoices",
                columns: new[] { "tenant_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lab_tests_tenant_id_code",
                schema: "catalog",
                table: "lab_tests",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_number_series_tenant_id_kind",
                schema: "platform",
                table: "number_series",
                columns: new[] { "tenant_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_utc",
                schema: "outbox",
                table: "outbox_messages",
                column: "processed_at_utc",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_full_name",
                schema: "patients",
                table: "patients",
                columns: new[] { "tenant_id", "full_name" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_mobile",
                schema: "patients",
                table: "patients",
                columns: new[] { "tenant_id", "mobile" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_national_id",
                schema: "patients",
                table: "patients",
                columns: new[] { "tenant_id", "national_id" },
                unique: true,
                filter: "national_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_patient_number",
                schema: "patients",
                table: "patients",
                columns: new[] { "tenant_id", "patient_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_invoice_id",
                schema: "billing",
                table: "payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_sample_conditions_sample_type_id",
                schema: "catalog",
                table: "sample_conditions",
                column: "sample_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_sample_types_tenant_id_name",
                schema: "catalog",
                table: "sample_types",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_samples_tenant_id_barcode",
                schema: "visits",
                table: "samples",
                columns: new[] { "tenant_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_samples_tenant_id_state",
                schema: "visits",
                table: "samples",
                columns: new[] { "tenant_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_samples_visit_id",
                schema: "visits",
                table: "samples",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_subdomain",
                schema: "platform",
                table: "tenants",
                column: "subdomain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_tests_visit_id",
                schema: "visits",
                table: "visit_tests",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_status",
                schema: "visits",
                table: "visits",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_visit_number",
                schema: "visits",
                table: "visits",
                columns: new[] { "tenant_id", "visit_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lab_tests",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "number_series",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "outbox");

            migrationBuilder.DropTable(
                name: "patients",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "sample_conditions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "samples",
                schema: "visits");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "visit_tests",
                schema: "visits");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "sample_types",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "visits",
                schema: "visits");
        }
    }
}
