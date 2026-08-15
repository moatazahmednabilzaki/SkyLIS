namespace SkyLIS.Domain.Visits;

/// <summary>Canonical visit/order pipeline (SRS Rev 2.0, Appendix A).</summary>
public enum VisitStatus
{
    Registered = 0,
    Collected = 1,
    Received = 2,
    InProcess = 3,
    Validated = 4,
    Reported = 5,
    Closed = 6,
    Cancelled = 7,
}

/// <summary>Canonical sample pipeline incl. the Rev 2.0 reservation states (P07.1).</summary>
public enum SampleState
{
    Reserved = 0,
    ConditionPending = 1,
    ReadyToCollect = 2,
    Collected = 3,
    Received = 4,
    InProcess = 5,
    Completed = 6,
    Rejected = 7,
}

public enum VisitTestStatus
{
    AwaitingSample = 0,
    Pending = 1,
    InProcess = 2,
    Entered = 3,
    TechValid = 4,
    MedValid = 5,
    Reported = 6,
    Cancelled = 7,
}
