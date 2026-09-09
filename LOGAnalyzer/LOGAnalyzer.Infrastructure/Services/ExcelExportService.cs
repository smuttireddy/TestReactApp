using ClosedXML.Excel;
using LOGAnalyzer.Core.Interfaces;
using LOGAnalyzer.Core.Models;

namespace LOGAnalyzer.Infrastructure.Services;

/// <summary>
/// Generates an Excel (.xlsx) workbook from a collection of LogErrorEntry records.
/// Uses ClosedXML for workbook creation.
/// </summary>
public class ExcelExportService : IExcelExportService
{
    private static readonly string[] Headers =
    [
        "FileName", "Timestamp", "ErrorCode", "Severity", "Category",
        "ErrorLine", "RootCause", "Solution", "RecommendedAction"
    ];

    public byte[] Export(IEnumerable<LogErrorEntry> entries, string fileName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Log Analysis");

        // --- Header row ---
        for (int col = 1; col <= Headers.Length; col++)
        {
            var cell = sheet.Cell(1, col);
            cell.Value = Headers[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // --- Data rows ---
        int row = 2;
        foreach (var entry in entries)
        {
            sheet.Cell(row, 1).Value = entry.FileName;
            sheet.Cell(row, 2).Value = entry.Timestamp;
            sheet.Cell(row, 3).Value = entry.ErrorCode;
            sheet.Cell(row, 4).Value = entry.Severity;
            sheet.Cell(row, 5).Value = entry.Category;
            sheet.Cell(row, 6).Value = entry.ErrorLine;
            sheet.Cell(row, 7).Value = entry.RootCause;
            sheet.Cell(row, 8).Value = entry.Solution;
            sheet.Cell(row, 9).Value = entry.RecommendedAction;

            // Colour-code severity
            var severityColor = entry.Severity switch
            {
                "Critical" => XLColor.FromHtml("#FEE2E2"),
                "High"     => XLColor.FromHtml("#FFEDD5"),
                "Medium"   => XLColor.FromHtml("#FEF9C3"),
                _          => XLColor.FromHtml("#DCFCE7")
            };
            sheet.Row(row).Style.Fill.BackgroundColor = severityColor;
            row++;
        }

        // Auto-fit columns
        sheet.Columns().AdjustToContents();

        // Add title above headers
        sheet.Row(1).InsertRowsAbove(1);
        var titleCell = sheet.Cell(1, 1);
        titleCell.Value = $"Log Analysis Report — {fileName}  |  Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 13;
        sheet.Range(1, 1, 1, Headers.Length).Merge();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
