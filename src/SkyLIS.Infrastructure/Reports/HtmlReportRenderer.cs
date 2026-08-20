using System.Net;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkyLIS.Application.Reports;
using SkyLIS.Domain.Reports;

namespace SkyLIS.Infrastructure.Reports;

/// <summary>
/// Renders both faces of one report: the PDF (QuestPDF, Community license) is the
/// immutable hash-stamped artifact of record delivered to patients; the self-contained
/// bilingual (EN/AR) HTML is the portal preview of the same content. Arabic strings
/// render in the HTML preview; the PDF uses Latin labels until an Arabic-capable font
/// ships with the binary.
/// </summary>
internal sealed class HtmlReportRenderer : IReportRenderer
{
    static HtmlReportRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private const string Navy = "#101d2c";
    private const string Blue = "#0284c7";
    private const string Slate = "#5a6472";
    private const string Line = "#e7f4fd";

    public byte[] RenderPdf(ReportContent content, ReportKind kind, string reportNumber, int version, DateTimeOffset nowUtc)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontSize(9.5f).FontColor("#26303b"));

                if (kind != ReportKind.Final)
                {
                    page.Foreground().AlignCenter().AlignMiddle()
                        .Text(kind == ReportKind.Interim ? "INTERIM — NOT FINAL" : "AMENDED")
                        .FontSize(46).Bold()
                        .FontColor(kind == ReportKind.Interim ? "#b26a0022" : "#b91c1c22");
                }

                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text(content.TenantLegalName).FontSize(16).Bold().FontColor(Navy);
                            left.Item().Text("LABORATORY REPORT").FontSize(8).Bold().FontColor(Blue).LetterSpacing(0.15f);
                        });
                        row.ConstantItem(220).Column(right =>
                        {
                            right.Item().AlignRight().Text($"{reportNumber} · v{version} · {kind.ToString().ToUpperInvariant()}")
                                .FontFamily(Fonts.Consolas).FontSize(10).Bold();
                            right.Item().AlignRight().Text($"Issued {nowUtc:yyyy-MM-dd HH:mm} UTC").FontColor(Slate);
                        });
                    });
                    column.Item().PaddingTop(6).BorderBottom(2).BorderColor(Blue);
                });

                page.Content().PaddingVertical(10).Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Patient: ").SemiBold();
                            text.Span($"{content.PatientFullName} ({content.PatientNumber})");
                        });
                        row.ConstantItem(160).AlignRight().Text($"{content.Gender} · {content.Age} y");
                    });
                    column.Item().PaddingBottom(8).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Visit: ").SemiBold();
                            text.Span(content.VisitNumber).FontFamily(Fonts.Consolas);
                        });
                        row.ConstantItem(220).AlignRight()
                            .Text($"Registered {content.VisitRegisteredAtUtc:yyyy-MM-dd HH:mm} UTC").FontColor(Slate);
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(64);
                            columns.RelativeColumn(1.1f);
                            columns.ConstantColumn(52);
                            columns.RelativeColumn(0.9f);
                            columns.ConstantColumn(64);
                            columns.RelativeColumn(1.6f);
                        });

                        table.Header(header =>
                        {
                            foreach (var title in new[] { "Test", "Result", "Unit", "Reference", "Flag", "Interpretation" })
                                header.Cell().Background(Navy).Padding(5)
                                    .Text(title).FontColor("#ffffff").FontSize(8.5f).Bold();
                        });

                        foreach (var line in content.Results)
                        {
                            var flagColor = line.Flag switch
                            {
                                "Normal" => "#177245",
                                "Low" or "High" => "#b26a00",
                                _ => "#b91c1c",
                            };
                            table.Cell().BorderBottom(0.5f).BorderColor(Line).Padding(5)
                                .Text(line.TestCode).FontFamily(Fonts.Consolas).Bold();
                            table.Cell().BorderBottom(0.5f).BorderColor(Line).Padding(5).Column(cell =>
                            {
                                cell.Item().AlignRight().Text($"{line.Value}").FontFamily(Fonts.Consolas);
                                if (line.IsAmended)
                                {
                                    cell.Item().AlignRight()
                                        .Text($"AMENDED — was {line.ValueBeforeAmendment}: {line.AmendmentReason}")
                                        .FontSize(7).Bold().FontColor("#b91c1c");
                                }
                            });
                            table.Cell().BorderBottom(0.5f).BorderColor(Line).Padding(5).Text(line.Unit);
                            table.Cell().BorderBottom(0.5f).BorderColor(Line).Padding(5)
                                .Text($"{(line.RefLow?.ToString() ?? "·")}–{(line.RefHigh?.ToString() ?? "·")}")
                                .FontFamily(Fonts.Consolas);
                            table.Cell().BorderBottom(0.5f).BorderColor(Line).Padding(5)
                                .Text(line.Flag).Bold().FontColor(flagColor);
                            table.Cell().BorderBottom(0.5f).BorderColor(Line).Padding(5)
                                .Text(line.InterpretiveComment ?? "");
                        }
                    });
                });

                page.Footer().PaddingTop(8).BorderTop(0.5f).BorderColor("#dbe6f0").Column(column =>
                {
                    if (content.FooterNote is not null)
                        column.Item().Text(content.FooterNote).FontSize(8).SemiBold();
                    column.Item().Text(
                        "Electronically signed results (FR-SYS-002). Verify authenticity without exposing content: "
                        + "scan the report QR or open /api/v1/public/reports/<id>/verify — the content hash is "
                        + "printed on issue and never changes. Sky LIS · one finalized report per visit is the "
                        + "accession of record.").FontSize(7).FontColor(Slate);
                    column.Item().AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(7).FontColor(Slate));
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    public string RenderHtml(ReportContent content, ReportKind kind, string reportNumber, int version, DateTimeOffset nowUtc)
    {
        var rows = new StringBuilder();
        foreach (var line in content.Results)
        {
            var flagColor = line.Flag switch
            {
                "Normal" => "#177245",
                "Low" or "High" => "#b26a00",
                _ => "#b91c1c",
            };
            // P09.5: an amended value is marked on the artifact with the superseded value.
            var amendedBadge = line.IsAmended
                ? $"""<div style="font-size:10px;color:#b91c1c;font-weight:700">AMENDED — was {line.ValueBeforeAmendment}: {WebUtility.HtmlEncode(line.AmendmentReason ?? "")}</div>"""
                : string.Empty;
            rows.Append($"""
                <tr>
                  <td class="mono"><b>{WebUtility.HtmlEncode(line.TestCode)}</b></td>
                  <td class="mono" style="text-align:right">{line.Value}{amendedBadge}</td>
                  <td>{WebUtility.HtmlEncode(line.Unit)}</td>
                  <td class="mono">{(line.RefLow is null ? "·" : line.RefLow)}–{(line.RefHigh is null ? "·" : line.RefHigh)}</td>
                  <td style="color:{flagColor};font-weight:700">{WebUtility.HtmlEncode(line.Flag)}</td>
                  <td>{WebUtility.HtmlEncode(line.InterpretiveComment ?? "")}</td>
                </tr>
                """);
        }

        var watermark = kind switch
        {
            ReportKind.Interim =>
                "<div style=\"position:fixed;top:40%;left:15%;font-size:70px;color:rgba(178,106,0,.12);transform:rotate(-20deg);font-weight:800\">INTERIM — NOT FINAL</div>",
            ReportKind.Amended =>
                "<div style=\"position:fixed;top:40%;left:22%;font-size:70px;color:rgba(185,28,28,.12);transform:rotate(-20deg);font-weight:800\">AMENDED</div>",
            _ => string.Empty,
        };

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>{{WebUtility.HtmlEncode(reportNumber)}} v{{version}} — {{WebUtility.HtmlEncode(content.TenantLegalName)}}</title>
            <style>
              body { font-family: 'Segoe UI', system-ui, sans-serif; color: #26303b; margin: 40px; font-size: 13px; }
              .head { display: flex; justify-content: space-between; border-bottom: 3px solid #0284c7; padding-bottom: 12px; }
              h1 { font-size: 20px; color: #101d2c; margin: 0; }
              .badge { font-size: 10px; font-weight: 700; letter-spacing: .1em; color: #0284c7; }
              table { width: 100%; border-collapse: collapse; margin-top: 16px; }
              th { background: #101d2c; color: #fff; text-align: left; padding: 7px 9px; font-size: 11px; }
              td { padding: 7px 9px; border-bottom: 1px solid #e7f4fd; }
              .mono { font-family: Consolas, monospace; }
              .meta { display: grid; grid-template-columns: 1fr 1fr; gap: 4px; margin-top: 14px; font-size: 12px; }
              .foot { margin-top: 26px; border-top: 1px solid #dbe6f0; padding-top: 10px; font-size: 10px; color: #5a6472; }
            </style>
            </head>
            <body>
            {{watermark}}
            <div class="head">
              <div>
                <h1>{{WebUtility.HtmlEncode(content.TenantLegalName)}}</h1>
                <div class="badge">LABORATORY REPORT — تقرير المختبر</div>
              </div>
              <div style="text-align:right">
                <div class="mono"><b>{{WebUtility.HtmlEncode(reportNumber)}}</b> · v{{version}} · {{kind.ToString().ToUpperInvariant()}}</div>
                <div>Issued {{nowUtc:yyyy-MM-dd HH:mm}} UTC</div>
              </div>
            </div>
            <div class="meta">
              <span><b>Patient — المريض:</b> {{WebUtility.HtmlEncode(content.PatientFullName)}} ({{WebUtility.HtmlEncode(content.PatientNumber)}})</span>
              <span style="text-align:right"><b>Sex/Age:</b> {{WebUtility.HtmlEncode(content.Gender)}} · {{content.Age}} y</span>
              <span><b>Visit — الزيارة:</b> {{WebUtility.HtmlEncode(content.VisitNumber)}}</span>
              <span style="text-align:right"><b>Registered:</b> {{content.VisitRegisteredAtUtc:yyyy-MM-dd HH:mm}} UTC</span>
            </div>
            <table>
              <tr><th>Test — التحليل</th><th style="text-align:right">Result — النتيجة</th><th>Unit</th><th>Reference — المرجع</th><th>Flag</th><th>Interpretation — التفسير</th></tr>
              {{rows}}
            </table>
            <div class="foot">
              {{(content.FooterNote is null ? "" : $"<div><b>{WebUtility.HtmlEncode(content.FooterNote)}</b></div>")}}
              {{(content.FooterNoteAr is null ? "" : $"<div dir=\"rtl\"><b>{WebUtility.HtmlEncode(content.FooterNoteAr)}</b></div>")}}
              Electronically signed results (FR-SYS-002). Verify authenticity without exposing content:
              scan the report QR or open /api/v1/public/reports/&lt;id&gt;/verify — the content hash is printed on issue and never changes.
              Sky LIS · one finalized report per visit is the accession of record.
            </div>
            </body>
            </html>
            """;
    }
}
