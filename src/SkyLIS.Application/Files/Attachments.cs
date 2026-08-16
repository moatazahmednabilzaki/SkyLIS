using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Files;

namespace SkyLIS.Application.Files;

public sealed record AttachmentDto(
    Guid Id, string EntityType, Guid EntityId, string FileName, string ContentType,
    int SizeBytes, DateTimeOffset UploadedAtUtc);

public sealed record AttachmentContentDto(string FileName, string ContentType, byte[] Content);

public interface IAttachmentQueries
{
    Task<IReadOnlyList<AttachmentDto>> ListAsync(string entityType, Guid entityId, CancellationToken ct = default);
}

/// <summary>FR-SYS-007: upload a file against a visit, patient, or result (base64 body, 5 MB cap).</summary>
public sealed record UploadAttachmentCommand(
    string EntityType, Guid EntityId, string FileName, string ContentType, string ContentBase64)
    : ICommand<AttachmentDto>, IRequirePermission
{
    public string Permission => "attachments.attachment.manage";
}

internal sealed class UploadAttachmentValidator : AbstractValidator<UploadAttachmentCommand>
{
    public UploadAttachmentValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty();
        RuleFor(x => x.EntityId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ContentBase64).NotEmpty()
            .Must(content => content.Length <= 7 * 1024 * 1024) // base64 of the 5 MB cap
            .WithMessage("Attachments are capped at 5 MB in Phase 1.");
    }
}

internal sealed class UploadAttachmentHandler : IRequestHandler<UploadAttachmentCommand, AttachmentDto>
{
    private readonly IAttachmentRepository _attachments;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public UploadAttachmentHandler(
        IAttachmentRepository attachments, ITenantContext tenant, ICurrentUser user, IClock clock)
    {
        _attachments = attachments;
        _tenant = tenant;
        _user = user;
        _clock = clock;
    }

    public Task<AttachmentDto> Handle(UploadAttachmentCommand request, CancellationToken ct)
    {
        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            throw new Domain.Common.DomainException("The file content must be valid base64.");
        }

        var attachment = Attachment.Upload(
            Guid.CreateVersion7(), _tenant.TenantId, request.EntityType, request.EntityId,
            request.FileName, request.ContentType, content, _user.UserId, _clock.UtcNow);
        _attachments.Add(attachment);

        return Task.FromResult(new AttachmentDto(
            attachment.Id, attachment.EntityType, attachment.EntityId, attachment.FileName,
            attachment.ContentType, attachment.SizeBytes, attachment.UploadedAtUtc));
    }
}

public sealed record ListAttachmentsQuery(string EntityType, Guid EntityId)
    : IQuery<IReadOnlyList<AttachmentDto>>, IRequirePermission
{
    public string Permission => "attachments.attachment.read";
}

internal sealed class ListAttachmentsHandler : IRequestHandler<ListAttachmentsQuery, IReadOnlyList<AttachmentDto>>
{
    private readonly IAttachmentQueries _queries;
    public ListAttachmentsHandler(IAttachmentQueries queries) => _queries = queries;

    public Task<IReadOnlyList<AttachmentDto>> Handle(ListAttachmentsQuery request, CancellationToken ct) =>
        _queries.ListAsync(request.EntityType, request.EntityId, ct);
}

public sealed record GetAttachmentContentQuery(Guid AttachmentId)
    : IQuery<AttachmentContentDto>, IRequirePermission
{
    public string Permission => "attachments.attachment.read";
}

internal sealed class GetAttachmentContentHandler : IRequestHandler<GetAttachmentContentQuery, AttachmentContentDto>
{
    private readonly IAttachmentRepository _attachments;
    public GetAttachmentContentHandler(IAttachmentRepository attachments) => _attachments = attachments;

    public async Task<AttachmentContentDto> Handle(GetAttachmentContentQuery request, CancellationToken ct)
    {
        var attachment = await _attachments.GetAsync(request.AttachmentId, ct)
            ?? throw new NotFoundException("Attachment", request.AttachmentId);
        return new AttachmentContentDto(attachment.FileName, attachment.ContentType, attachment.Content);
    }
}
