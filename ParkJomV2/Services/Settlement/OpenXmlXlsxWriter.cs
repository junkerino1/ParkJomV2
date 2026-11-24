using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace ParkJomV2.Services.Settlement;

public sealed record WorkbookSheet(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows);

public static class OpenXmlXlsxWriter
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static byte[] BuildWorkbook(IReadOnlyList<WorkbookSheet> sheets)
    {
        if (sheets.Count == 0)
            throw new ArgumentException("At least one worksheet is required.", nameof(sheets));

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteXmlEntry(archive, "[Content_Types].xml", BuildContentTypes(sheets.Count));
            WriteXmlEntry(archive, "_rels/.rels", BuildRootRelationships());
            WriteXmlEntry(archive, "xl/workbook.xml", BuildWorkbookXml(sheets));
            WriteXmlEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships(sheets.Count));
            WriteXmlEntry(archive, "xl/styles.xml", BuildStylesXml());

            for (var i = 0; i < sheets.Count; i++)
            {
                WriteXmlEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildWorksheetXml(sheets[i]));
            }
        }

        return stream.ToArray();
    }

    private static void WriteXmlEntry(ZipArchive archive, string entryPath, XDocument document)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static XDocument BuildContentTypes(int sheetCount)
    {
        var elements = new List<XElement>
        {
            new(ContentTypesNs + "Default",
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new(ContentTypesNs + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new(ContentTypesNs + "Override",
                new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new(ContentTypesNs + "Override",
                new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))
        };

        for (var i = 1; i <= sheetCount; i++)
        {
            elements.Add(new XElement(ContentTypesNs + "Override",
                new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ContentTypesNs + "Types", elements));
    }

    private static XDocument BuildRootRelationships()
    {
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(PkgRelNs + "Relationships",
                new XElement(PkgRelNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static XDocument BuildWorkbookXml(IReadOnlyList<WorkbookSheet> sheets)
    {
        var sheetElements = sheets.Select((sheet, index) =>
            new XElement(MainNs + "sheet",
                new XAttribute("name", SanitizeSheetName(sheet.Name, index + 1)),
                new XAttribute("sheetId", index + 1),
                new XAttribute(RelNs + "id", $"rId{index + 1}")));

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(MainNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", RelNs.NamespaceName),
                new XElement(MainNs + "sheets", sheetElements)));
    }

    private static XDocument BuildWorkbookRelationships(int sheetCount)
    {
        var relationships = new List<XElement>();
        for (var i = 1; i <= sheetCount; i++)
        {
            relationships.Add(new XElement(PkgRelNs + "Relationship",
                new XAttribute("Id", $"rId{i}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{i}.xml")));
        }

        relationships.Add(new XElement(PkgRelNs + "Relationship",
            new XAttribute("Id", $"rId{sheetCount + 1}"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            new XAttribute("Target", "styles.xml")));

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(PkgRelNs + "Relationships", relationships));
    }

    private static XDocument BuildStylesXml()
    {
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(MainNs + "styleSheet",
                new XElement(MainNs + "numFmts",
                    new XAttribute("count", "1"),
                    new XElement(MainNs + "numFmt",
                        new XAttribute("numFmtId", "164"),
                        new XAttribute("formatCode", "#,##0.00"))),
                new XElement(MainNs + "fonts",
                    new XAttribute("count", "2"),
                    new XElement(MainNs + "font",
                        new XElement(MainNs + "sz", new XAttribute("val", "10")),
                        new XElement(MainNs + "name", new XAttribute("val", "Calibri"))),
                    new XElement(MainNs + "font",
                        new XElement(MainNs + "b"),
                        new XElement(MainNs + "sz", new XAttribute("val", "10")),
                        new XElement(MainNs + "color", new XAttribute("rgb", "FFFFFFFF")),
                        new XElement(MainNs + "name", new XAttribute("val", "Calibri")))),
                new XElement(MainNs + "fills",
                    new XAttribute("count", "3"),
                    new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "none"))),
                    new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "gray125"))),
                    new XElement(MainNs + "fill",
                        new XElement(MainNs + "patternFill",
                            new XAttribute("patternType", "solid"),
                            new XElement(MainNs + "fgColor", new XAttribute("rgb", "FF1E3A8A"))))),
                new XElement(MainNs + "borders",
                    new XAttribute("count", "1"),
                    new XElement(MainNs + "border",
                        new XElement(MainNs + "left"),
                        new XElement(MainNs + "right"),
                        new XElement(MainNs + "top"),
                        new XElement(MainNs + "bottom"),
                        new XElement(MainNs + "diagonal"))),
                new XElement(MainNs + "cellStyleXfs",
                    new XAttribute("count", "1"),
                    new XElement(MainNs + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"))),
                new XElement(MainNs + "cellXfs",
                    new XAttribute("count", "3"),
                    // Style 0: default text/integer
                    new XElement(MainNs + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"),
                        new XAttribute("xfId", "0")),
                    // Style 1: dark blue bold header
                    new XElement(MainNs + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "1"),
                        new XAttribute("fillId", "2"),
                        new XAttribute("borderId", "0"),
                        new XAttribute("xfId", "0"),
                        new XAttribute("applyFont", "1"),
                        new XAttribute("applyFill", "1")),
                    // Style 2: 2-decimal currency/number
                    new XElement(MainNs + "xf",
                        new XAttribute("numFmtId", "164"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"),
                        new XAttribute("xfId", "0"),
                        new XAttribute("applyNumberFormat", "1")))));
    }

    private static XDocument BuildWorksheetXml(WorkbookSheet sheet)
    {
        var columnCount = sheet.Headers.Count;
        var widths = new int[columnCount];
        for (var c = 0; c < columnCount; c++)
        {
            widths[c] = Math.Clamp(sheet.Headers[c].Length + 4, 12, 48);
        }

        foreach (var row in sheet.Rows)
        {
            for (var c = 0; c < Math.Min(columnCount, row.Count); c++)
            {
                var length = FormatDisplayLength(row[c]);
                if (length + 3 > widths[c])
                    widths[c] = Math.Min(48, length + 3);
            }
        }

        var colsElement = new XElement(MainNs + "cols",
            widths.Select((w, idx) => new XElement(MainNs + "col",
                new XAttribute("min", idx + 1),
                new XAttribute("max", idx + 1),
                new XAttribute("width", w.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("customWidth", "1"))));

        var sheetData = new XElement(MainNs + "sheetData");

        // Header row (row 1)
        var headerRow = new XElement(MainNs + "row", new XAttribute("r", "1"));
        for (var c = 0; c < columnCount; c++)
        {
            headerRow.Add(CreateTextCell(GetColumnLetter(c) + "1", sheet.Headers[c], styleIndex: 1));
        }
        sheetData.Add(headerRow);

        // Data rows
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var rowNumber = r + 2;
            var rowElement = new XElement(MainNs + "row", new XAttribute("r", rowNumber.ToString(CultureInfo.InvariantCulture)));
            var values = sheet.Rows[r];
            for (var c = 0; c < columnCount; c++)
            {
                var val = c < values.Count ? values[c] : null;
                rowElement.Add(CreateCell(GetColumnLetter(c) + rowNumber.ToString(CultureInfo.InvariantCulture), val));
            }
            sheetData.Add(rowElement);
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(MainNs + "worksheet",
                colsElement,
                sheetData));
    }

    private static XElement CreateCell(string cellRef, object? value)
    {
        return value switch
        {
            null => CreateTextCell(cellRef, string.Empty, styleIndex: 0),
            decimal d => CreateNumericCell(cellRef, d.ToString("0.00", CultureInfo.InvariantCulture), styleIndex: 2),
            double db when double.IsFinite(db) => CreateNumericCell(cellRef, db.ToString("0.00", CultureInfo.InvariantCulture), styleIndex: 2),
            float fl when float.IsFinite(fl) => CreateNumericCell(cellRef, fl.ToString("0.00", CultureInfo.InvariantCulture), styleIndex: 2),
            int i => CreateNumericCell(cellRef, i.ToString(CultureInfo.InvariantCulture), styleIndex: 0),
            long l => CreateNumericCell(cellRef, l.ToString(CultureInfo.InvariantCulture), styleIndex: 0),
            DateTime dt => CreateTextCell(cellRef, dt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture), styleIndex: 0),
            bool b => CreateTextCell(cellRef, b ? "Yes" : "No", styleIndex: 0),
            _ => CreateTextCell(cellRef, value.ToString() ?? string.Empty, styleIndex: 0)
        };
    }

    private static XElement CreateNumericCell(string cellRef, string numericString, int styleIndex)
    {
        return new XElement(MainNs + "c",
            new XAttribute("r", cellRef),
            new XAttribute("s", styleIndex.ToString(CultureInfo.InvariantCulture)),
            new XElement(MainNs + "v", numericString));
    }

    private static XElement CreateTextCell(string cellRef, string text, int styleIndex)
    {
        var sanitized = StripInvalidXmlChars(text);
        return new XElement(MainNs + "c",
            new XAttribute("r", cellRef),
            new XAttribute("t", "inlineStr"),
            new XAttribute("s", styleIndex.ToString(CultureInfo.InvariantCulture)),
            new XElement(MainNs + "is",
                new XElement(MainNs + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    sanitized)));
    }

    private static string StripInvalidXmlChars(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static int FormatDisplayLength(object? value)
        => value switch
        {
            null => 0,
            DateTime => 23,
            decimal d => d.ToString("0.00", CultureInfo.InvariantCulture).Length,
            _ => (value.ToString() ?? string.Empty).Length
        };

    private static string GetColumnLetter(int zeroBasedIndex)
    {
        var index = zeroBasedIndex;
        var letters = new StringBuilder();
        while (index >= 0)
        {
            letters.Insert(0, (char)('A' + (index % 26)));
            index = (index / 26) - 1;
        }
        return letters.ToString();
    }

    private static string SanitizeSheetName(string name, int fallbackIndex)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var cleaned = new string((name ?? string.Empty).Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrEmpty(cleaned)) cleaned = $"Sheet{fallbackIndex}";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
