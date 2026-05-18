using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Generators;

/// <summary>
/// Generates valid .xlsx (OpenXML SpreadsheetML) canary files with patient registry data.
/// Opens correctly in Microsoft Excel.
/// </summary>
public sealed class XlsxCanaryGenerator : ICanaryFileGenerator
{
    private static readonly string[] LastNames =
        ["Mballa", "Ngoa", "Kamga", "Tchamba", "Bekolo", "Etoundi", "Mvondo", "Fotso", "Nkoulou", "Tagne"];

    private static readonly string[] FirstNames =
        ["Jean-Pierre", "Marie-Claire", "Paul", "Françoise", "Emmanuel", "Bernadette", "Samuel", "Cécile", "Joseph", "Anne-Marie"];

    private static readonly string[] Diagnostics =
        ["Paludisme", "Hypertension", "Diabète type 2", "Anémie", "Tuberculose", "Drépanocytose", "Gastrite", "Asthme"];

    /// <inheritdoc />
    public string Extension => ".xlsx";

    /// <inheritdoc />
    public byte[] Generate(string template)
    {
        using var stream = new MemoryStream();
        using (SpreadsheetDocument doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData());

            Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Registre Patients"
            });

            SheetData sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>()!;

            // Header row
            Row header = new() { RowIndex = 1 };
            header.Append(CreateCell("A1", "ID"));
            header.Append(CreateCell("B1", "Nom"));
            header.Append(CreateCell("C1", "Prénom"));
            header.Append(CreateCell("D1", "Date Naissance"));
            header.Append(CreateCell("E1", "N° Dossier"));
            header.Append(CreateCell("F1", "Diagnostic"));
            sheetData.Append(header);

            // Generate 15 patient rows
            for (uint i = 2; i <= 16; i++)
            {
                string lastName = Pick(LastNames);
                string firstName = Pick(FirstNames);
                string dob = $"{RandomInt(1, 28):D2}/{RandomInt(1, 12):D2}/{RandomInt(1950, 2005)}";
                string fileNum = $"DM-{RandomInt(10000, 99999)}";
                string diagnosis = Pick(Diagnostics);

                Row row = new() { RowIndex = i };
                row.Append(CreateCell($"A{i}", $"P-{RandomInt(1000, 9999)}"));
                row.Append(CreateCell($"B{i}", lastName));
                row.Append(CreateCell($"C{i}", firstName));
                row.Append(CreateCell($"D{i}", dob));
                row.Append(CreateCell($"E{i}", fileNum));
                row.Append(CreateCell($"F{i}", diagnosis));
                sheetData.Append(row);
            }
        }

        return stream.ToArray();
    }

    private static Cell CreateCell(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.String,
        CellValue = new CellValue(value)
    };

    private static string Pick(string[] array) =>
        array[RandomNumberGenerator.GetInt32(array.Length)];

    private static int RandomInt(int min, int max) =>
        RandomNumberGenerator.GetInt32(min, max + 1);
}
