using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Visits;

public sealed record VisitRegistered(Guid VisitId, Guid TenantId, Guid PatientId, string VisitNumber) : DomainEvent;
public sealed record SampleReserved(Guid VisitId, Guid SampleId, Guid TenantId, DateTimeOffset ReadyAtUtc) : DomainEvent;
public sealed record SampleCollected(Guid VisitId, Guid SampleId, Guid TenantId) : DomainEvent;
public sealed record SampleReceived(Guid VisitId, Guid SampleId, Guid TenantId) : DomainEvent;
public sealed record SampleRejected(Guid VisitId, Guid SampleId, Guid TenantId, string ReasonCode) : DomainEvent;
public sealed record VisitCancelled(Guid VisitId, Guid TenantId, string Reason) : DomainEvent;
