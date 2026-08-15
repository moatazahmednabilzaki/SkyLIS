using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SkyLIS.Domain.Results;

namespace SkyLIS.Infrastructure.Persistence;

internal sealed class TestResultConfig : IEntityTypeConfiguration<TestResult>
{
    public void Configure(EntityTypeBuilder<TestResult> b)
    {
        b.ToTable("test_results", "results");
        b.HasKey(r => r.Id);
        b.Property(r => r.TenantId).IsRequired();
        b.HasIndex(r => new { r.TenantId, r.VisitTestId });
        b.HasIndex(r => new { r.TenantId, r.PatientId, r.TestCode });
        b.HasIndex(r => new { r.TenantId, r.Status });
        b.Property(r => r.TestCode).HasMaxLength(20).IsRequired();
        b.Property(r => r.Value).HasPrecision(14, 4);
        b.Property(r => r.PreviousValue).HasPrecision(14, 4);
        b.Property(r => r.Unit).HasMaxLength(20).IsRequired();
        b.Property(r => r.Flag).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.InterpretiveComment).HasMaxLength(2000);
        b.Property(r => r.SignatureHash).HasMaxLength(64);
        b.Property(r => r.RerunReason).HasMaxLength(300);
        b.HasOne(r => r.Critical).WithOne().HasForeignKey<CriticalNotification>(c => c.TestResultId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Critical).AutoInclude();
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class CriticalNotificationConfig : IEntityTypeConfiguration<CriticalNotification>
{
    public void Configure(EntityTypeBuilder<CriticalNotification> b)
    {
        b.ToTable("critical_notifications", "results");
        b.HasKey(c => c.Id);
        b.Property(c => c.TenantId).IsRequired();
        b.HasIndex(c => new { c.TenantId, c.State });
        b.Property(c => c.State).HasConversion<string>().HasMaxLength(30);
        b.Property(c => c.CalledPerson).HasMaxLength(200);
        b.Property(c => c.CalledPhone).HasMaxLength(20);
    }
}
