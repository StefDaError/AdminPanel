using AdminPanel.Models;
using ClosedXML.Excel;

namespace AdminPanel.Services;

public static class ExcelExporter
{
    public static void Export(
        string filePath,
        IEnumerable<AgentRecord> agents,
        IEnumerable<(string DomainName, ComponentItem Item)> components)
    {
        using var wb = new XLWorkbook();

        // --- Лист «Агенты» ---
        var ws = wb.Worksheets.Add("Агенты");
        string[] headers1 = { "Имя узла", "IP-адрес", "Платформа", "Версия ОС", "Последняя активность", "Снимков" };
        for (int i = 0; i < headers1.Length; i++)
            ws.Cell(1, i + 1).Value = headers1[i];

        int row = 2;
        foreach (var a in agents)
        {
            ws.Cell(row, 1).Value = a.DomainName;
            ws.Cell(row, 2).Value = a.IpAddress ?? "";
            ws.Cell(row, 3).Value = a.Platform ?? "";
            ws.Cell(row, 4).Value = a.OsVersion ?? "";
            if (a.LastSeen.HasValue)
            {
                ws.Cell(row, 5).Value = a.LastSeen.Value;
                ws.Cell(row, 5).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
            }
            ws.Cell(row, 6).Value = a.SnapshotCount;
            row++;
        }
        StyleSheet(ws, headers1.Length);

        // --- Лист «Комплектующие» ---
        var ws2 = wb.Worksheets.Add("Комплектующие");
        string[] headers2 = { "Имя узла", "Категория", "Название", "Характеристики" };
        for (int i = 0; i < headers2.Length; i++)
            ws2.Cell(1, i + 1).Value = headers2[i];

        row = 2;
        foreach (var (domain, c) in components)
        {
            ws2.Cell(row, 1).Value = domain;
            ws2.Cell(row, 2).Value = c.Category;
            ws2.Cell(row, 3).Value = c.Name;
            ws2.Cell(row, 4).Value = c.Specs;
            row++;
        }
        StyleSheet(ws2, headers2.Length);

        wb.SaveAs(filePath);
    }

    private static void StyleSheet(IXLWorksheet ws, int columns)
    {
        var header = ws.Range(1, 1, 1, columns);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#0E639C");
        header.Style.Font.FontColor = XLColor.White;

        ws.SheetView.FreezeRows(1);
        if (ws.LastRowUsed() is { } last && last.RowNumber() > 1)
            ws.Range(1, 1, last.RowNumber(), columns).SetAutoFilter();
        ws.Columns().AdjustToContents();
    }
}