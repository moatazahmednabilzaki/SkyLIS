using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Visits;

public sealed record VisitRegistered(Guid VisitId, Guid TenantId, Guid PatientId, string VisitNumber) : DomainEvent, ITenantEvent;
public sealed record SampleReserved(Guid VisitId, Guid SampleId, Guid TenantId, DateTimeOffset ReadyAtUtc) : DomainEvent, ITenantEvent;
public sealed record SampleCollected(Guid VisitId, Guid SampleId, Guid TenantId) : DomainEvent, ITenantEvent;
public sealed record SampleReceived(Guid VisitId, Guid SampleId, Guid TenantId) : DomainEvent, ITenantEvent;
public sealed record SampleRejected(Guid VisitId, Guid SampleId, Guid TenantId, string ReasonCode) : DomainEvent, ITenantEvent;
public sealed record VisitCancelled(Guid VisitId, Guid TenantId, string Reason) : DomainEvent, ITenantEvent;
public sealed record PatientInformedOfRejection(Guid VisitId, Guid SampleId, Guid TenantId) : DomainEvent, ITenantEvent;
