// Copyright (C) 2026 Lewandowskista
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using QAssistant.Models;

namespace QAssistant.Services
{
    public class ParsedCsvData
    {
        public List<string> Headers { get; set; } = [];
        public List<Dictionary<string, string>> Rows { get; set; } = [];
    }

    public static class CsvImportService
    {
        // Ordered list of (propertyName, displayName) for the mapping ComboBoxes.
        public static readonly IReadOnlyList<(string Field, string Display)> TestCaseFields =
        [
            ("(Ignore)",                          "(Ignore)"),
            (nameof(TestCase.Title),              "Title"),
            (nameof(TestCase.TestCaseId),         "Test Case ID"),
            (nameof(TestCase.PreConditions),      "Pre-Conditions"),
            (nameof(TestCase.TestSteps),          "Test Steps"),
            (nameof(TestCase.TestData),           "Test Data"),
            (nameof(TestCase.ExpectedResult),     "Expected Result"),
            (nameof(TestCase.ActualResult),       "Actual Result"),
            (nameof(TestCase.Status),             "Status"),
            (nameof(TestCase.Priority),           "Priority"),
            (nameof(TestCase.SourceIssueId),      "Source Issue ID"),
        ];

        // ── Public entry point ───────────────────────────────────────────────

        public static ParsedCsvData Parse(string filePath)
        {
            return Path.GetExtension(filePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? ParseXlsx(filePath)
                : ParseCsv(filePath);
        }

        // ── CSV parser (RFC 4180 compliant) ──────────────────────────────────

        private static ParsedCsvData ParseCsv(string filePath)
        {
            var result = new ParsedCsvData();
            using var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);

            var headerLine = reader.ReadLine();
            if (headerLine == null) return result;

            result.Headers = SplitCsvLine(headerLine);

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var values = SplitCsvLine(line);
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < result.Headers.Count; i++)
                    row[result.Headers[i]] = i < values.Count ? values[i] : string.Empty;

                result.Rows.Add(row);
            }

            return result;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString().Trim());
            return fields;
        }

        // ── XLSX parser (ZIP + OpenXML, no external library) ─────────────────

        private static ParsedCsvData ParseXlsx(string filePath)
        {
            var result = new ParsedCsvData();

            using var archive = ZipFile.OpenRead(filePath);

            var sharedStrings = ReadSharedStrings(archive);

            var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            if (sheetEntry == null) return result;

            using var stream = sheetEntry.Open();
            var doc = XDocument.Load(stream);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            bool firstRow = true;
            foreach (var rowEl in doc.Descendants(ns + "row"))
            {
                var cells = rowEl.Elements(ns + "c").ToList();
                if (cells.Count == 0) continue;

                int maxCol = cells.Max(c =>
                    ColLetterToIndex(GetColRef(c.Attribute("r")?.Value ?? "A"))) + 1;

                int width = firstRow ? maxCol : Math.Max(maxCol, result.Headers.Count);
                var values = new string[width];
                for (int i = 0; i < width; i++) values[i] = string.Empty;

                foreach (var cell in cells)
                {
                    var cellRef = cell.Attribute("r")?.Value ?? string.Empty;
                    int col = ColLetterToIndex(GetColRef(cellRef));
                    if (col >= 0 && col < width)
                        values[col] = GetXlsxCellValue(cell, sharedStrings, ns);
                }

                if (firstRow)
                {
                    result.Headers = [.. values];
                    firstRow = false;
                }
                else
                {
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < result.Headers.Count; i++)
                        row[result.Headers[i]] = i < values.Length ? values[i] : string.Empty;
                    result.Rows.Add(row);
                }
            }

            return result;
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var list = new List<string>();
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return list;

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            list.AddRange(doc.Descendants(ns + "si")
                .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))));

            return list;
        }

        private static string GetXlsxCellValue(XElement cell, List<string> sharedStrings, XNamespace ns)
        {
            var type = cell.Attribute("t")?.Value;
            var v = cell.Element(ns + "v")?.Value ?? string.Empty;

            if (type == "s" && int.TryParse(v, out var idx) && idx >= 0 && idx < sharedStrings.Count)
                return sharedStrings[idx];

            if (type == "inlineStr")
                return cell.Element(ns + "is")?.Element(ns + "t")?.Value ?? string.Empty;

            return v;
        }

        private static string GetColRef(string cellRef) =>
            new(cellRef.TakeWhile(char.IsLetter).ToArray());

        private static int ColLetterToIndex(string col)
        {
            int result = 0;
            foreach (char c in col.ToUpper())
                result = result * 26 + (c - 'A' + 1);
            return result - 1;
        }

        // ── Column auto-detection ─────────────────────────────────────────────

        public static Dictionary<string, string> AutoDetectMappings(IEnumerable<string> headers)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(TestCase.Title)] =
                    ["Title", "Name", "Summary", "Test Name", "Test Case Name", "Test Case", "Subject"],
                [nameof(TestCase.TestCaseId)] =
                    ["ID", "Test ID", "Test Case ID", "TestCaseId", "Identifier", "Key", "Case ID", "Ref", "Number"],
                [nameof(TestCase.PreConditions)] =
                    ["PreConditions", "Pre-conditions", "Pre Conditions", "Preconditions", "Setup", "Prerequisites"],
                [nameof(TestCase.TestSteps)] =
                    ["TestSteps", "Test Steps", "Steps", "Steps to Reproduce", "Actions", "Test Actions", "Step Description"],
                [nameof(TestCase.TestData)] =
                    ["TestData", "Test Data", "Data", "Input Data", "Test Input", "Inputs"],
                [nameof(TestCase.ExpectedResult)] =
                    ["ExpectedResult", "Expected Result", "Expected", "Expected Outcome", "Expected Output", "Pass Criteria", "Expected Behaviour"],
                [nameof(TestCase.ActualResult)] =
                    ["ActualResult", "Actual Result", "Actual", "Actual Outcome", "Actual Output"],
                [nameof(TestCase.Status)] =
                    ["Status", "Result", "Test Result", "Execution Status", "Outcome", "Run Status"],
                [nameof(TestCase.Priority)] =
                    ["Priority", "Severity", "Importance", "Level", "Criticality"],
                [nameof(TestCase.SourceIssueId)] =
                    ["SourceIssueId", "Issue ID", "Issue Key", "Linked Issue", "Related Issue", "Jira ID", "Linear ID", "Requirement"],
            };

            foreach (var header in headers)
            {
                foreach (var (field, aliasList) in aliases)
                {
                    if (aliasList.Any(a => string.Equals(a, header, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!map.ContainsKey(header))
                            map[header] = field;
                        break;
                    }
                }
            }

            return map;
        }

        // ── Row → TestCase mapping ────────────────────────────────────────────

        public static TestCase MapToTestCase(Dictionary<string, string> row, Dictionary<string, string> columnMap)
        {
            var tc = new TestCase();

            foreach (var (csvCol, tcField) in columnMap)
            {
                if (tcField == "(Ignore)") continue;
                if (!row.TryGetValue(csvCol, out var val) || string.IsNullOrWhiteSpace(val)) continue;

                switch (tcField)
                {
                    case nameof(TestCase.TestCaseId):     tc.TestCaseId = val; break;
                    case nameof(TestCase.Title):           tc.Title = val; break;
                    case nameof(TestCase.PreConditions):  tc.PreConditions = val; break;
                    case nameof(TestCase.TestSteps):      tc.TestSteps = val; break;
                    case nameof(TestCase.TestData):       tc.TestData = val; break;
                    case nameof(TestCase.ExpectedResult): tc.ExpectedResult = val; break;
                    case nameof(TestCase.ActualResult):   tc.ActualResult = val; break;
                    case nameof(TestCase.SourceIssueId):  tc.SourceIssueId = val; break;
                    case nameof(TestCase.Status):
                        if (Enum.TryParse<TestCaseStatus>(val, ignoreCase: true, out var status))
                            tc.Status = status;
                        break;
                    case nameof(TestCase.Priority):
                        if (Enum.TryParse<TestCasePriority>(val, ignoreCase: true, out var priority))
                            tc.Priority = priority;
                        break;
                }
            }

            return tc;
        }
    }
}
