namespace SkyLIS.Domain.Users;

/// <summary>
/// The immutable system role catalog (§4.2, P02.2): named permission bundles.
/// Tenant-cloned custom roles are a later slice; system roles themselves never change
/// at runtime — they ship with the codebase as the single source of truth.
/// </summary>
public static class RoleCatalog
{
    public const string TenantAdmin = "TenantAdmin";
    public const string LabDirector = "LabDirector";
    public const string Supervisor = "Supervisor";
    public const string Technologist = "Technologist";
    public const string Receptionist = "Receptionist";
    public const string Cashier = "Cashier";
    public const string QualityManager = "QualityManager";

    private static readonly Dictionary<string, string[]> Bundles = new()
    {
        [TenantAdmin] =
        [
            "users.user.create", "users.user.read",
            "org.branch.read", "org.branch.manage",
            "catalog.catalog.read",
            "catalog.sampletype.create", "catalog.test.create", "catalog.test.update", "catalog.test.approve",
            "patients.patient.create", "patients.patient.read",
            "orders.visit.create", "orders.visit.read", "orders.visit.cancel",
            "samples.sample.collect", "samples.sample.receive", "samples.sample.reject",
            "samples.sample.informPatient",
            "results.result.enter", "results.result.validateTechnical", "results.result.validateMedical",
            "reports.report.render", "reports.report.deliver", "reports.report.read",
            "billing.payment.capture", "billing.invoice.adjust", "billing.refund.approve", "billing.shift.manage",
            "analytics.dashboard.read", "audit.trail.read",
        ],
        [LabDirector] =
        [
            "patients.patient.read", "orders.visit.read",
            "org.branch.read", "catalog.catalog.read",
            "results.result.validateTechnical", "results.result.validateMedical",
            "catalog.test.approve",
            "reports.report.render", "reports.report.deliver", "reports.report.read",
            "analytics.dashboard.read", "audit.trail.read",
        ],
        [Supervisor] =
        [
            "patients.patient.read", "orders.visit.read",
            "org.branch.read", "catalog.catalog.read",
            "samples.sample.receive", "samples.sample.reject",
            "results.result.enter", "results.result.validateTechnical",
            "reports.report.read", "analytics.dashboard.read",
        ],
        [Technologist] =
        [
            "patients.patient.read", "orders.visit.read",
            "org.branch.read", "catalog.catalog.read",
            "samples.sample.receive", "samples.sample.reject",
            "results.result.enter",
        ],
        [Receptionist] =
        [
            "org.branch.read", "catalog.catalog.read",
            "patients.patient.create", "patients.patient.read",
            "orders.visit.create", "orders.visit.read", "orders.visit.cancel",
            "samples.sample.collect", "samples.sample.informPatient",
            "billing.payment.capture", "billing.invoice.adjust",
            "reports.report.read", "reports.report.deliver",
        ],
        [Cashier] =
        [
            "patients.patient.read", "orders.visit.read", "billing.payment.capture",
            "billing.shift.manage", "org.branch.read",
        ],
        [QualityManager] =
        [
            "patients.patient.read", "orders.visit.read",
            "org.branch.read", "catalog.catalog.read",
            "reports.report.read", "analytics.dashboard.read", "audit.trail.read",
        ],
    };

    public static bool Exists(string role) => Bundles.ContainsKey(role);

    public static IReadOnlyCollection<string> PermissionsOf(string role) =>
        Bundles.TryGetValue(role, out var permissions) ? permissions : [];

    public static IReadOnlyCollection<string> AllRoles => Bundles.Keys.ToList();
}
