using System.Net;
using System.Text;
using SkyLIS.Application.Reports;
using SkyLIS.Domain.Reports;

namespace SkyLIS.Infrastructure.Reports;

/// <summary>
/// Renders the immutable report artifact as a self-contained bilingual (EN/AR) HTML
/// document in the Sky LIS brand. A PDF converter implements the same IReportRenderer
/// port in a later slice; the hash-and-store contract is unchanged.
/// </summary>
internal sealed class HtmlReportRenderer : IReportRenderer
{
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
