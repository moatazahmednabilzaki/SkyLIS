using System.Text;
using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;

namespace SkyLIS.Application.Catalog;

public sealed record CatalogImportResultDto(int Created, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// FR-SYS-009: CSV catalog import. Expected header:
/// Code,Name,Department,SampleTypeName,ConditionName,Price,Currency
/// Rows with an existing code are SKIPPED (idempotent re-import); created tests arrive
/// as Draft and walk the normal review/approval flow. Simple CSV — no quoted commas.
/// </summary>
public sealed record ImportTestsCommand(string Csv) : ICommand<CatalogImportResultDto>, IRequirePermission
{
    public string Permission => "catalog.test.create";
}

internal sealed class ImportTestsValidator : AbstractValidator<ImportTestsCommand>
{
    public ImportTestsValidator()
    {
        RuleFor(x => x.Csv).NotEmpty().MaximumLength(512 * 1024)
            .WithMessage("The CSV content is required (max 512 KB).");
    }
}

internal sealed class ImportTestsHandler : IRequestHandler<ImportTestsCommand, CatalogImportResultDto>
{
    private readonly ILabTestRepository _tests;
    private readonly ISampleTypeRepository _sampleTypes;
    private readonly ITenantContext _tenant;

    public ImportTestsHandler(ILabTestRepository tests, ISampleTypeRepository sampleTypes, ITenantContext tenant)
    {
        _tests = tests;
        _sampleTypes = sampleTypes;
        _tenant = tenant;
    }

    public async Task<CatalogImportResultDto> Handle(ImportTestsCommand request, CancellationToken ct)
    {
        var lines = request.Csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            throw new DomainException("The CSV needs a header row and at least one data row.");

        var header = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant()).ToArray();
        string[] expected = ["code", "name", "department", "sampletypename", "conditionname", "price", "currency"];
        if (!expected.SequenceEqual(header))
            throw new DomainException(
                "Header must be exactly: Code,Name,Department,SampleTypeName,ConditionName,Price,Currency.");

        var created = 0;
        var skipped = 0;
        var errors = new List<string>();
        var createdCodes = new HashSet<string>();

        for (var i = 1; i < lines.Length; i++)
        {
            var cells = lines[i].Split(',').Select(c => c.Trim()).ToArray();
            if (cells.Length != 7)
            {
                errors.Add($"Row {i + 1}: expected 7 columns, got {cells.Length}.");
                continue;
            }

            var code = cells[0].ToUpperInvariant();
            try
            {
                if (createdCodes.Contains(code) || await _tests.CodeExistsAsync(code, ct))
                {
                    skipped++;
                    continue;
                }

                var sampleType = await _sampleTypes.GetByNameAsync(cells[3], ct)
                    ?? throw new DomainException($"sample type '{cells[3]}' does not exist");
                Guid? conditionId = null;
                if (!string.IsNullOrWhiteSpace(cells[4]))
                {
                    conditionId = (sampleType.Conditions.FirstOrDefault(c =>
                            c.Name.Equals(cells[4], StringComparison.OrdinalIgnoreCase))
                        ?? throw new DomainException($"condition '{cells[4]}' does not exist on '{cells[3]}'")).Id;
                }
                if (!decimal.TryParse(cells[5], out var price))
                    throw new DomainException($"'{cells[5]}' is not a price");

                _tests.Add(LabTest.CreateTenantTest(
                    Guid.CreateVersion7(), _tenant.TenantId, code, cells[1], cells[2],
                    sampleType.Id, conditionId, Money.Of(price, cells[6])));
                createdCodes.Add(code);
                created++;
            }
            catch (DomainException ex)
            {
                errors.Add($"Row {i + 1} ({code}): {ex.Message}");
            }
        }

        return new CatalogImportResultDto(created, skipped, errors);
    }
}

/// <summary>FR-SYS-009: CSV catalog export (round-trips through the import header).</summary>
public sealed record ExportTestsQuery : IQuery<string>, IRequirePermission
{
    public string Permission => "catalog.catalog.read";
}

internal sealed class ExportTestsHandler : IRequestHandler<ExportTestsQuery, string>
{
    private readonly ICatalogQueries _catalog;

    public ExportTestsHandler(ICatalogQueries catalog) => _catalog = catalog;

    public async Task<string> Handle(ExportTestsQuery request, CancellationToken ct)
    {
        var tests = await _catalog.ListTestsAsync(null, ct);
        var sampleTypes = await _catalog.ListSampleTypesAsync(ct);
        var typeById = sampleTypes.ToDictionary(s => s.Id);

        var csv = new StringBuilder();
        csv.AppendLine("Code,Name,Department,SampleTypeName,ConditionName,Price,Currency");
        foreach (var test in tests)
        {
            var sampleType = typeById.GetValueOrDefault(test.SampleTypeId);
            var condition = test.RequiredConditionId is null
                ? ""
                : sampleType?.Conditions.FirstOrDefault(c => c.Id == test.RequiredConditionId)?.Name ?? "";
            csv.AppendLine(string.Join(',',
                test.Code, test.Name.Replace(',', ' '), test.Department.Replace(',', ' '),
                sampleType?.Name.Replace(',', ' ') ?? "", condition.Replace(',', ' '),
                test.Price?.ToString() ?? "", test.Currency ?? ""));
        }
        return csv.ToString();
    }
}
