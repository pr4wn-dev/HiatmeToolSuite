using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Builds a printable .docx corrective-action form without requiring Word or OpenXML NuGet.
    /// </summary>
    internal static class DriverDisciplineDocument
    {
        public static void Save(string path, DriverDisciplineRecord r)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required.", nameof(path));
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
                File.Delete(path);

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
                WriteEntry(zip, "_rels/.rels", RelsXml);
                WriteEntry(zip, "word/_rels/document.xml.rels", DocumentRelsXml);
                WriteEntry(zip, "word/styles.xml", StylesXml);
                WriteEntry(zip, "word/settings.xml", SettingsXml);
                WriteEntry(zip, "word/document.xml", BuildDocumentXml(r));
            }
        }

        /// <summary>Build .docx bytes in a temp file (for library upload without SaveFileDialog).</summary>
        public static byte[] ToBytes(DriverDisciplineRecord r)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "dd_" + Guid.NewGuid().ToString("N") + ".docx");
            try
            {
                Save(tmp, r);
                return File.ReadAllBytes(tmp);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
            }
        }

        private static void WriteEntry(ZipArchive zip, string name, string xml)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                writer.Write(xml);
        }

        private static string BuildDocumentXml(DriverDisciplineRecord r)
        {
            var sb = new StringBuilder(16 * 1024);
            sb.Append(DocumentOpen);

            // Title
            sb.Append(P("HIATME", "Title", center: true, bold: true, size: 32));
            sb.Append(P("Driver Corrective Action Notice", "Heading1", center: true, bold: true, size: 28));
            sb.Append(P("Dashcam / operational discipline write-up — retain in personnel file", "Normal", center: true, size: 18, italic: true));
            sb.Append(EmptyP());

            // Meta grid
            sb.Append(StartTable(new[] { 2340, 2340, 2340, 2340 }));
            sb.Append(Row(
                CellLabelValue("Case / reference #", r.CaseNumber),
                CellLabelValue("Notice date", r.NoticeDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)),
                CellLabelValue("Prepared by", r.PreparedBy),
                CellLabelValue("Department", r.Department)));
            sb.Append(EndTable());
            sb.Append(EmptyP());

            sb.Append(P("1. Employee", "Heading2", bold: true, size: 22));
            sb.Append(StartTable(new[] { 4680, 4680 }));
            sb.Append(Row(
                CellLabelValue("Driver name", r.DriverName),
                CellLabelValue("Employee ID", r.EmployeeId)));
            sb.Append(Row(
                CellLabelValue("Vehicle", r.Vehicle),
                CellLabelValue("Supervisor", r.SupervisorName)));
            sb.Append(EndTable());
            sb.Append(EmptyP());

            sb.Append(P("2. Incident", "Heading2", bold: true, size: 22));
            sb.Append(StartTable(new[] { 3120, 3120, 3120 }));
            sb.Append(Row(
                CellLabelValue("Incident date", r.IncidentDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)),
                CellLabelValue("Approximate time", string.IsNullOrWhiteSpace(r.IncidentTime) ? "—" : r.IncidentTime),
                CellLabelValue("Trip / client ref", string.IsNullOrWhiteSpace(r.TripOrClientRef) ? "—" : r.TripOrClientRef)));
            sb.Append(EndTable());
            sb.Append(StartTable(new[] { 9360 }));
            sb.Append(Row(CellLabelValue("Location / area", string.IsNullOrWhiteSpace(r.Location) ? "—" : r.Location)));
            sb.Append(EndTable());
            sb.Append(EmptyP());

            sb.Append(P("3. Violation type(s)", "Heading2", bold: true, size: 22));
            sb.Append(P("Mark all that apply based on dashcam and investigation:", "Normal", size: 18, italic: true));
            var chosen = new HashSet<string>(
                (r.Violations ?? new List<string>()).Where(v => !string.IsNullOrWhiteSpace(v)),
                StringComparer.OrdinalIgnoreCase);
            foreach (string option in DriverDisciplineOptions.Violations)
            {
                bool on = chosen.Contains(option);
                sb.Append(P((on ? "☑  " : "☐  ") + option, "Normal", size: 20));
            }
            sb.Append(EmptyP());

            sb.Append(P("4. Action level", "Heading2", bold: true, size: 22));
            string level = (r.ActionLevel ?? "").Trim();
            foreach (string option in DriverDisciplineOptions.ActionLevels)
            {
                bool on = string.Equals(option, level, StringComparison.OrdinalIgnoreCase);
                sb.Append(P((on ? "☑  " : "☐  ") + option, "Normal", size: 20));
            }
            sb.Append(EmptyP());

            sb.Append(P("5. What the footage shows", "Heading2", bold: true, size: 22));
            sb.Append(MultiLineBlock(string.IsNullOrWhiteSpace(r.FootageSummary) ? r.Narrative : r.FootageSummary));
            sb.Append(EmptyP());

            sb.Append(P("6. Full narrative / investigation notes", "Heading2", bold: true, size: 22));
            sb.Append(MultiLineBlock(r.Narrative));
            sb.Append(EmptyP());

            sb.Append(P("7. Dashcam evidence", "Heading2", bold: true, size: 22));
            sb.Append(StartTable(new[] { 9360 }));
            sb.Append(Row(CellLabelValue("Footage folder", string.IsNullOrWhiteSpace(r.FootageFolder) ? "—" : r.FootageFolder)));
            sb.Append(EndTable());
            var clips = (r.ClipPaths ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (clips.Count == 0)
            {
                sb.Append(P("☐  Clip file(s) attached / listed below:", "Normal", size: 20));
                sb.Append(P("—", "Normal", size: 20));
            }
            else
            {
                sb.Append(P("Clip file(s):", "Normal", size: 20, bold: true));
                foreach (string clip in clips)
                    sb.Append(P("•  " + Path.GetFileName(clip) + "    (" + clip + ")", "Normal", size: 18));
            }
            sb.Append(EmptyP());

            sb.Append(P("8. Policy / rule cited", "Heading2", bold: true, size: 22));
            sb.Append(MultiLineBlock(r.PolicyCited));
            sb.Append(EmptyP());

            sb.Append(P("9. Prior related history", "Heading2", bold: true, size: 22));
            sb.Append(MultiLineBlock(string.IsNullOrWhiteSpace(r.PriorHistory) ? "None noted." : r.PriorHistory));
            sb.Append(EmptyP());

            sb.Append(P("10. Corrective action required", "Heading2", bold: true, size: 22));
            sb.Append(MultiLineBlock(r.CorrectiveAction));
            if (!string.IsNullOrWhiteSpace(r.FollowUpDate))
                sb.Append(P("Follow-up / review date: " + r.FollowUpDate.Trim(), "Normal", size: 20, bold: true));
            sb.Append(EmptyP());

            sb.Append(P("11. Driver statement", "Heading2", bold: true, size: 22));
            sb.Append(P("Driver may attach a written response. Optional notes below:", "Normal", size: 18, italic: true));
            sb.Append(MultiLineBlock(string.IsNullOrWhiteSpace(r.DriverStatement) ? "(left blank for driver to complete)" : r.DriverStatement));
            sb.Append(EmptyP());

            sb.Append(P("12. Acknowledgments", "Heading2", bold: true, size: 22));
            sb.Append(P(
                "I acknowledge that I have reviewed this notice and the referenced dashcam evidence. " +
                "My signature confirms receipt, not necessarily agreement with every finding. " +
                "I understand further violations may lead to additional discipline up to and including termination.",
                "Normal", size: 18, italic: true));
            sb.Append(EmptyP());

            sb.Append(StartTable(new[] { 4680, 4680 }));
            sb.Append(Row(
                CellLabelValue("Driver signature", "______________________________"),
                CellLabelValue("Date", "____________")));
            sb.Append(Row(
                CellLabelValue("Supervisor signature", "______________________________"),
                CellLabelValue("Date", "____________")));
            sb.Append(Row(
                CellLabelValue("Witness (optional)", "______________________________"),
                CellLabelValue("Date", "____________")));
            sb.Append(EndTable());
            sb.Append(EmptyP());
            sb.Append(P("Company copy · Driver copy · Personnel file", "Normal", center: true, size: 16, italic: true));

            sb.Append(DocumentClose);
            return sb.ToString();
        }

        private static string MultiLineBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return P("—", "Normal", size: 20);

            var sb = new StringBuilder();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string line in lines)
                sb.Append(P(string.IsNullOrWhiteSpace(line) ? " " : line, "Normal", size: 20));
            return sb.ToString();
        }

        private static string StartTable(int[] widths)
        {
            var sb = new StringBuilder();
            sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"9360\" w:type=\"dxa\"/><w:tblBorders>");
            sb.Append("<w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("</w:tblBorders><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid>");
            foreach (int w in widths)
                sb.Append("<w:gridCol w:w=\"").Append(w).Append("\"/>");
            sb.Append("</w:tblGrid>");
            return sb.ToString();
        }

        private static string EndTable() => "</w:tbl>";

        private static string Row(params string[] cells)
        {
            var sb = new StringBuilder();
            sb.Append("<w:tr>");
            foreach (string c in cells) sb.Append(c);
            sb.Append("</w:tr>");
            return sb.ToString();
        }

        private static string CellLabelValue(string label, string value)
        {
            string v = string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
            return "<w:tc><w:tcPr><w:tcW w:w=\"0\" w:type=\"auto\"/><w:tcMar>" +
                   "<w:top w:w=\"60\" w:type=\"dxa\"/><w:left w:w=\"80\" w:type=\"dxa\"/>" +
                   "<w:bottom w:w=\"60\" w:type=\"dxa\"/><w:right w:w=\"80\" w:type=\"dxa\"/></w:tcMar></w:tcPr>" +
                   P(label, "Normal", size: 16, bold: true) +
                   P(v, "Normal", size: 20) +
                   "</w:tc>";
        }

        private static string EmptyP() => "<w:p><w:pPr><w:spacing w:after=\"60\"/></w:pPr></w:p>";

        private static string P(string text, string style, bool center = false, bool bold = false, bool italic = false, int size = 20)
        {
            var sb = new StringBuilder();
            sb.Append("<w:p><w:pPr>");
            if (!string.IsNullOrEmpty(style))
                sb.Append("<w:pStyle w:val=\"").Append(style).Append("\"/>");
            if (center)
                sb.Append("<w:jc w:val=\"center\"/>");
            sb.Append("<w:spacing w:after=\"40\" w:line=\"276\" w:lineRule=\"auto\"/>");
            sb.Append("</w:pPr><w:r><w:rPr>");
            if (bold) sb.Append("<w:b/>");
            if (italic) sb.Append("<w:i/>");
            sb.Append("<w:sz w:val=\"").Append(size).Append("\"/><w:szCs w:val=\"").Append(size).Append("\"/>");
            sb.Append("</w:rPr><w:t xml:space=\"preserve\">").Append(Xml(text)).Append("</w:t></w:r></w:p>");
            return sb.ToString();
        }

        private static string Xml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private const string DocumentOpen =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<w:body>";

        private const string DocumentClose =
            "<w:sectPr>" +
            "<w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
            "<w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" " +
            "w:header=\"360\" w:footer=\"360\" w:gutter=\"0\"/>" +
            "</w:sectPr></w:body></w:document>";

        private const string ContentTypesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
            "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
            "</Types>";

        private const string RelsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>";

        private const string DocumentRelsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
            "</Relationships>";

        private const string StylesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">" +
            "<w:name w:val=\"Normal\"/><w:qFormat/>" +
            "<w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Calibri\"/>" +
            "<w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/>" +
            "<w:rPr><w:b/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/>" +
            "<w:rPr><w:b/><w:sz w:val=\"28\"/><w:szCs w:val=\"28\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/>" +
            "<w:pPr><w:spacing w:before=\"120\" w:after=\"60\"/></w:pPr>" +
            "<w:rPr><w:b/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr></w:style>" +
            "</w:styles>";

        private const string SettingsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:defaultTabStop w:val=\"720\"/>" +
            "</w:settings>";
    }
}
