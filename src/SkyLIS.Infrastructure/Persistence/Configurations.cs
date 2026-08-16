using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Org;
using SkyLIS.Domain.Patients;
using SkyLIS.Domain.Platform;
using SkyLIS.Domain.Tenants;
using SkyLIS.Domain.Visits;
using SkyLIS.Infrastructure.Outbox;

namespace SkyLIS.Infrastructure.Persistence;

// Schema per module (SRS Rev 2.0 §10). Status enums stored as strings for auditability.
// Optimistic concurrency via PostgreSQL xmin on every aggregate root.

internal sealed class TenantConfig : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants", "platform");
        b.HasKey(t => t.Id);
        b.Property(t => t.LegalName).HasMaxLength(200).IsRequired();
        b.Property(t => t.Subdomain).HasMaxLength(40).IsRequired();
        b.HasIndex(t => t.Subdomain).IsUnique();
        b.Property(t => t.CountryCode).HasMaxLength(2).IsRequired();
        b.Property(t => t.PlanCode).HasMaxLength(40).IsRequired();
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.IsolationTier).HasConversion<string>().HasMaxLength(30);
        b.Property(t => t.SuspensionReason).HasMaxLength(500);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class BranchConfig : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("branches", "org");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Code).HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.HasMany(x => x.Departments).WithOne().HasForeignKey(d => d.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Departments).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class DepartmentConfig : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("branch_departments", "org");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Code).HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.Code }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
    }
}

internal sealed class TenantSettingConfig : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> b)
    {
        b.ToTable("tenant_settings", "org");
        b.HasKey(s => s.Id);
        b.Property(s => s.TenantId).IsRequired();
        b.Property(s => s.Key).HasMaxLength(80).IsRequired();
        b.HasIndex(s => new { s.TenantId, s.Key }).IsUnique();
        b.Property(s => s.Value).HasMaxLength(2000).IsRequired();
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class CountryPackConfig : IEntityTypeConfiguration<CountryPack>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<CountryPack> b)
    {
        b.ToTable("country_packs", "platform");
        b.HasKey(x => x.Id);
        b.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        b.HasIndex(x => x.CountryCode).IsUnique();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        // The pack content is a document, not a relation: persisted as one jsonb column.
        b.Property(x => x.SampleTypes)
            .HasColumnName("sample_types")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<PackSampleType>>(v, JsonOptions)!,
                new ValueComparer<IReadOnlyList<PackSampleType>>(
                    (a, c) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(c, JsonOptions),
                    v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                    v => JsonSerializer.Deserialize<List<PackSampleType>>(
                        JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!));
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class PlanConfig : IEntityTypeConfiguration<SkyLIS.Domain.Platform.Plan>
{
    public void Configure(EntityTypeBuilder<SkyLIS.Domain.Platform.Plan> b)
    {
        b.ToTable("plans", "platform");
        b.HasKey(p => p.Id);
        b.Property(p => p.Code).HasMaxLength(40).IsRequired();
        b.HasIndex(p => p.Code).IsUnique();
        b.Property(p => p.Name).HasMaxLength(120).IsRequired();
        b.Property(p => p.MonthlyPrice).HasPrecision(12, 2);
        b.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class MasterTestConfig : IEntityTypeConfiguration<SkyLIS.Domain.Platform.MasterTest>
{
    public void Configure(EntityTypeBuilder<SkyLIS.Domain.Platform.MasterTest> b)
    {
        b.ToTable("master_tests", "platform");
        b.HasKey(m => m.Id);
        b.Property(m => m.Code).HasMaxLength(20).IsRequired();
        b.HasIndex(m => m.Code).IsUnique();
        b.Property(m => m.Name).HasMaxLength(200).IsRequired();
        b.Property(m => m.Department).HasMaxLength(80).IsRequired();
        b.Property(m => m.SampleTypeName).HasMaxLength(80).IsRequired();
        b.Property(m => m.ContainerName).HasMaxLength(80).IsRequired();
        b.Property(m => m.ConditionName).HasMaxLength(80);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class AttachmentConfig : IEntityTypeConfiguration<SkyLIS.Domain.Files.Attachment>
{
    public void Configure(EntityTypeBuilder<SkyLIS.Domain.Files.Attachment> b)
    {
        b.ToTable("attachments", "files");
        b.HasKey(a => a.Id);
        b.Property(a => a.TenantId).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(20).IsRequired();
        b.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        b.Property(a => a.FileName).HasMaxLength(200).IsRequired();
        b.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        b.Property(a => a.Content).IsRequired();
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class PatientConfig : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        b.ToTable("patients", "patients");
        b.HasKey(p => p.Id);
        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.PatientNumber).HasMaxLength(30).IsRequired();
        b.HasIndex(p => new { p.TenantId, p.PatientNumber }).IsUnique();
        b.Property(p => p.FullName).HasMaxLength(200).IsRequired();
        b.HasIndex(p => new { p.TenantId, p.FullName });
        b.Property(p => p.Sex).HasConversion<string>().HasMaxLength(10);
        b.Property(p => p.Mobile).HasConversion(v => v.Value, v => PhoneNumber.Of(v)).HasMaxLength(20);
        b.HasIndex(p => new { p.TenantId, p.Mobile });
        b.Property(p => p.NationalId).HasMaxLength(30);
        b.HasIndex(p => new { p.TenantId, p.NationalId }).IsUnique().HasFilter("national_id IS NOT NULL");
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class DataSubjectRequestConfig : IEntityTypeConfiguration<DataSubjectRequest>
{
    public void Configure(EntityTypeBuilder<DataSubjectRequest> b)
    {
        b.ToTable("data_subject_requests", "patients");
        b.HasKey(r => r.Id);
        b.Property(r => r.TenantId).IsRequired();
        b.HasIndex(r => new { r.TenantId, r.PatientId });
        b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Reason).HasMaxLength(300).IsRequired();
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class LabTestConfig : IEntityTypeConfiguration<LabTest>
{
    public void Configure(EntityTypeBuilder<LabTest> b)
    {
        b.ToTable("lab_tests", "catalog");
        b.HasKey(t => t.Id);
        b.Property(t => t.TenantId).IsRequired();
        b.Property(t => t.Code).HasMaxLength(20).IsRequired();
        b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        b.Property(t => t.Department).HasMaxLength(80).IsRequired();
        b.Property(t => t.Origin).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.OwnsOne(t => t.Price, money =>
        {
            money.Property(m => m.Amount).HasColumnName("price_amount").HasPrecision(12, 2);
            money.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });
        b.OwnsOne(t => t.ResultSchema, schema =>
        {
            schema.Property(s => s.Unit).HasColumnName("result_unit").HasMaxLength(20);
            schema.Property(s => s.RefLow).HasColumnName("ref_low").HasPrecision(14, 4);
            schema.Property(s => s.RefHigh).HasColumnName("ref_high").HasPrecision(14, 4);
            schema.Property(s => s.CriticalLow).HasColumnName("critical_low").HasPrecision(14, 4);
            schema.Property(s => s.CriticalHigh).HasColumnName("critical_high").HasPrecision(14, 4);
            schema.Property(s => s.AbsurdLow).HasColumnName("absurd_low").HasPrecision(14, 4);
            schema.Property(s => s.AbsurdHigh).HasColumnName("absurd_high").HasPrecision(14, 4);
            schema.Property(s => s.AutoVerify).HasColumnName("auto_verify");
            schema.Property(s => s.DeltaThresholdPercent).HasColumnName("delta_threshold_percent").HasPrecision(7, 2);
        });
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class SampleTypeConfig : IEntityTypeConfiguration<SampleType>
{
    public void Configure(EntityTypeBuilder<SampleType> b)
    {
        b.ToTable("sample_types", "catalog");
        b.HasKey(s => s.Id);
        b.Property(s => s.TenantId).IsRequired();
        b.Property(s => s.Name).HasMaxLength(80).IsRequired();
        b.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();
        b.Property(s => s.ContainerName).HasMaxLength(80).IsRequired();
        b.HasMany(s => s.Conditions).WithOne().HasForeignKey(c => c.SampleTypeId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(s => s.Conditions).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class SampleConditionConfig : IEntityTypeConfiguration<SampleCondition>
{
    public void Configure(EntityTypeBuilder<SampleCondition> b)
    {
        b.ToTable("sample_conditions", "catalog");
        b.HasKey(c => c.Id);
        b.Property(c => c.TenantId).IsRequired();
        b.Property(c => c.Name).HasMaxLength(80).IsRequired();
        b.Property(c => c.CompatibilityGroup).HasMaxLength(40).IsRequired();
    }
}

internal sealed class PanelConfig : IEntityTypeConfiguration<Panel>
{
    public void Configure(EntityTypeBuilder<Panel> b)
    {
        b.ToTable("panels", "catalog");
        b.HasKey(p => p.Id);
        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.Code).HasMaxLength(20).IsRequired();
        b.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.OwnsOne(p => p.Price, money =>
        {
            money.Property(m => m.Amount).HasColumnName("price_amount").HasPrecision(12, 2);
            money.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });
        b.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.PanelId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class PanelItemConfig : IEntityTypeConfiguration<PanelItem>
{
    public void Configure(EntityTypeBuilder<PanelItem> b)
    {
        b.ToTable("panel_items", "catalog");
        b.HasKey(i => i.Id);
        b.Property(i => i.TenantId).IsRequired();
        b.HasIndex(i => new { i.TenantId, i.PanelId, i.TestId }).IsUnique();
    }
}

internal sealed class VisitConfig : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> b)
    {
        b.ToTable("visits", "visits");
        b.HasKey(v => v.Id);
        b.Property(v => v.TenantId).IsRequired();
        b.Property(v => v.VisitNumber).HasMaxLength(30).IsRequired();
        b.HasIndex(v => new { v.TenantId, v.VisitNumber }).IsUnique();
        b.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(v => new { v.TenantId, v.Status });
        b.Property(v => v.StatReason).HasMaxLength(300);
        b.HasMany(v => v.Tests).WithOne().HasForeignKey("visit_id").OnDelete(DeleteBehavior.Cascade);
        b.HasMany(v => v.Samples).WithOne().HasForeignKey(s => s.VisitId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(v => v.Tests).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(v => v.Samples).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class VisitTestConfig : IEntityTypeConfiguration<VisitTest>
{
    public void Configure(EntityTypeBuilder<VisitTest> b)
    {
        b.ToTable("visit_tests", "visits");
        b.HasKey(t => t.Id);
        b.Property(t => t.TenantId).IsRequired();
        b.Property(t => t.TestCode).HasMaxLength(20).IsRequired();
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.OwnsOne(t => t.Price, money =>
        {
            money.Property(m => m.Amount).HasColumnName("price_amount").HasPrecision(12, 2);
            money.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });
    }
}

internal sealed class SampleConfig : IEntityTypeConfiguration<Sample>
{
    public void Configure(EntityTypeBuilder<Sample> b)
    {
        b.ToTable("samples", "visits");
        b.HasKey(s => s.Id);
        b.Property(s => s.TenantId).IsRequired();
        b.Property(s => s.Barcode).HasMaxLength(40).IsRequired();
        b.HasIndex(s => new { s.TenantId, s.Barcode }).IsUnique();
        b.Property(s => s.State).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(s => new { s.TenantId, s.State });
        b.Property(s => s.ConditionName).HasMaxLength(80);
        b.Property(s => s.RejectionReasonCode).HasMaxLength(60);
    }
}

internal sealed class InvoiceConfig : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices", "billing");
        b.HasKey(i => i.Id);
        b.Property(i => i.TenantId).IsRequired();
        b.Property(i => i.InvoiceNumber).HasMaxLength(30).IsRequired();
        b.HasIndex(i => new { i.TenantId, i.InvoiceNumber }).IsUnique();
        b.HasIndex(i => new { i.TenantId, i.VisitId });
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        b.OwnsOne(i => i.Total, money =>
        {
            money.Property(m => m.Amount).HasColumnName("total_amount").HasPrecision(12, 2);
            money.Property(m => m.Currency).HasColumnName("total_currency").HasMaxLength(3);
        });
        b.HasMany(i => i.Payments).WithOne().HasForeignKey("invoice_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(i => i.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class PaymentConfig : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", "billing");
        b.HasKey(p => p.Id);
        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.Method).HasMaxLength(20).IsRequired();
        b.Property(p => p.Reason).HasMaxLength(300);
        b.OwnsOne(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(12, 2);
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });
    }
}

internal sealed class CreditNoteConfig : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> b)
    {
        b.ToTable("credit_notes", "billing");
        b.HasKey(c => c.Id);
        b.Property(c => c.TenantId).IsRequired();
        b.Property(c => c.CreditNoteNumber).HasMaxLength(30).IsRequired();
        b.HasIndex(c => new { c.TenantId, c.CreditNoteNumber }).IsUnique();
        b.HasIndex(c => new { c.TenantId, c.InvoiceId });
        b.Property(c => c.Reason).HasMaxLength(400).IsRequired();
        b.OwnsOne(c => c.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(12, 2);
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class CashierShiftConfig : IEntityTypeConfiguration<CashierShift>
{
    public void Configure(EntityTypeBuilder<CashierShift> b)
    {
        b.ToTable("cashier_shifts", "billing");
        b.HasKey(s => s.Id);
        b.Property(s => s.TenantId).IsRequired();
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(s => new { s.TenantId, s.BranchId, s.Status });
        b.Property(s => s.DeclaredCash).HasPrecision(12, 2);
        b.Property(s => s.ExpectedCash).HasPrecision(12, 2);
        b.Property(s => s.Variance).HasPrecision(12, 2);
        b.OwnsOne(s => s.OpeningFloat, money =>
        {
            money.Property(m => m.Amount).HasColumnName("opening_float").HasPrecision(12, 2);
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class OutboxMessageConfig : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages", "outbox");
        b.HasKey(m => m.Id);
        b.Property(m => m.EventType).HasMaxLength(300).IsRequired();
        b.Property(m => m.Payload).IsRequired();
        b.Property(m => m.LastError).HasMaxLength(2000);
        b.HasIndex(m => m.ProcessedAtUtc).HasFilter("processed_at_utc IS NULL");
    }
}

internal sealed class InboxConsumptionConfig : IEntityTypeConfiguration<SkyLIS.Infrastructure.Outbox.InboxConsumption>
{
    public void Configure(EntityTypeBuilder<SkyLIS.Infrastructure.Outbox.InboxConsumption> b)
    {
        b.ToTable("inbox_consumptions", "outbox");
        b.HasKey(c => new { c.HandlerName, c.EventId });
        b.Property(c => c.HandlerName).HasMaxLength(300);
    }
}

internal sealed class NumberSeriesConfig : IEntityTypeConfiguration<NumberSeries>
{
    public void Configure(EntityTypeBuilder<NumberSeries> b)
    {
        b.ToTable("number_series", "platform");
        b.HasKey(n => n.Id);
        b.Property(n => n.Kind).HasMaxLength(30).IsRequired();
        b.HasIndex(n => new { n.TenantId, n.Kind }).IsUnique();
        b.Property<uint>("xmin").IsRowVersion();
    }
}
