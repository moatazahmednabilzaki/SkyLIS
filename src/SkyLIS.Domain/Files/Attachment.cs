using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Files;

/// <summary>
/// Tenant-owned aggregate (FR-SYS-007): a file attached to a clinical entity (visit,
/// patient, or result). Phase 1 stores content in the database (5 MB cap); an object
/// storage adapter swaps in behind the same repository port later.
/// </summary>
public sealed class Attachment : AggregateRoot, ITenantOwned
{
    public const int MaxSizeBytes = 5 * 1024 * 1024;
    private static readonly string[] EntityTypes = ["visit", "patient", "result"];

    public Guid TenantId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public int SizeBytes { get; private set; }
    public byte[] Content { get; private set; } = null!;
    public Guid? UploadedByUserId { get; private set; }
    public DateTimeOffset UploadedAtUtc { get; private set; }

    private Attachment() { } // EF

    public static Attachment Upload(
        Guid id, Guid tenantId, string entityType, Guid entityId,
        string fileName, string contentType, byte[] content, Guid? uploadedBy, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        var normalizedType = entityType?.Trim().ToLowerInvariant() ?? "";
        if (!EntityTypes.Contains(normalizedType))
            throw new DomainException($"Attachments can target: {string.Join(", ", EntityTypes)}.");
        if (entityId == Guid.Empty) throw new DomainException("The target entity id is required.");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 200)
            throw new DomainException("A file name up to 200 characters is required.");
        if (fileName.IndexOfAny(['/', '\\']) >= 0)
            throw new DomainException("The file name must not contain path separators.");
        if (string.IsNullOrWhiteSpace(contentType)) throw new DomainException("The content type is required.");
        if (content is null || content.Length == 0) throw new DomainException("The file content is empty.");
        if (content.Length > MaxSizeBytes)
            throw new DomainException($"Attachments are capped at {MaxSizeBytes / (1024 * 1024)} MB in Phase 1.");

        return new Attachment
        {
            Id = id,
            TenantId = tenantId,
            EntityType = normalizedType,
            EntityId = entityId,
            FileName = fileName.Trim(),
            ContentType = contentType.Trim(),
            SizeBytes = content.Length,
            Content = content,
            UploadedByUserId = uploadedBy,
            UploadedAtUtc = nowUtc,
        };
    }
}
