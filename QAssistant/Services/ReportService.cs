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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using QAssistant.Models;

namespace QAssistant.Services
{
    public static class ReportService
    {
        // â”€â”€ CSV Generation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public static string GenerateTestCasesCsv(
            Project project,
            List<TestPlan>? filterPlans = null)
        {
            var plans = filterPlans ?? project.TestPlans;
            var sb = new StringBuilder();

            sb.AppendLine("Test Plan ID,Test Plan Name,Test Case ID,Title,Status,Pre-Conditions,Test Steps,Test Data,Expected Result,Actual Result,Source,Generated At");

            foreach (var plan in plans.OrderBy(p => p.TestPlanId))
            {
                var cases = project.TestCases
                    .Where(tc => tc.TestPlanId == plan.Id)
                    .OrderBy(tc => tc.TestCaseId)
                    .ToList();

                foreach (var tc in cases)
                {
                    sb.Append(CsvEscape(plan.TestPlanId)).Append(',');
                    sb.Append(CsvEscape(plan.Name)).Append(',');
                    sb.Append(CsvEscape(tc.TestCaseId)).Append(',');
                    sb.Append(CsvEscape(tc.Title)).Append(',');
                    sb.Append(CsvEscape(tc.Status.ToString())).Append(',');
                    sb.Append(CsvEscape(tc.PreConditions)).Append(',');
                    sb.Append(CsvEscape(tc.TestSteps)).Append(',');
                    sb.Append(CsvEscape(tc.TestData)).Append(',');
                    sb.Append(CsvEscape(tc.ExpectedResult)).Append(',');
                    sb.Append(CsvEscape(tc.ActualResult)).Append(',');
                    sb.Append(CsvEscape(tc.Source.ToString())).Append(',');
                    sb.AppendLine(CsvEscape(tc.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss")));
                }
            }

            return sb.ToString();
        }

        public static string GenerateExecutionsCsv(Project project, List<TestExecution>? filterExecutions = null)
        {
            var executions = filterExecutions ?? project.TestExecutions;
            var sb = new StringBuilder();

            sb.AppendLine("Execution ID,Test Case ID,Test Case Title,Test Plan ID,Result,Actual Result,Notes,Executed At");

            var testCaseLookup = project.TestCases.ToDictionary(c => c.Id);
            var testPlanLookup = project.TestPlans.ToDictionary(p => p.Id);

            foreach (var exec in executions.OrderByDescending(e => e.ExecutedAt))
            {
                testCaseLookup.TryGetValue(exec.TestCaseId, out var tc);
                testPlanLookup.TryGetValue(exec.TestPlanId.GetValueOrDefault(), out var plan);

                sb.Append(CsvEscape(exec.ExecutionId)).Append(',');
                sb.Append(CsvEscape(tc?.TestCaseId ?? "N/A")).Append(',');
                sb.Append(CsvEscape(tc?.Title ?? "Deleted")).Append(',');
                sb.Append(CsvEscape(plan?.TestPlanId ?? "N/A")).Append(',');
                sb.Append(CsvEscape(exec.Result.ToString())).Append(',');
                sb.Append(CsvEscape(exec.ActualResult)).Append(',');
                sb.Append(CsvEscape(exec.Notes)).Append(',');
                sb.AppendLine(CsvEscape(exec.ExecutedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            }

            return sb.ToString();
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // Mitigate CSV formula injection: prefix cells that start with
            // characters that spreadsheet applications interpret as formulas.
            var sanitized = value;
            if (sanitized.Length > 0 && sanitized[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
                sanitized = "'" + sanitized;

            if (sanitized.Contains('"') || sanitized.Contains(',') || sanitized.Contains('\n') || sanitized.Contains('\r'))
                return $"\"{sanitized.Replace("\"", "\"\"")}\"";

            return sanitized;
        }

        // â”€â”€ PDF Generation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public static byte[] GenerateTestSummaryPdf(Project project, List<TestPlan>? filterPlans = null, List<TestExecution>? filterExecutions = null, string? criticalityAssessment = null)
        {
            var plans = filterPlans ?? project.TestPlans;
            var allCases = project.TestCases;
            var allExecs = filterExecutions ?? project.TestExecutions;

            var pdf = new PdfBuilder();

            // â”€â”€ Page 1: Cover / Summary â”€â”€
            var page = pdf.AddPage();
            float y = 760;
            float margin = 50;
            float contentWidth = 495; // 595 - 2*50

            // Title
            page.SetFont("Helvetica-Bold", 22);
            page.SetColor(0.1f, 0.1f, 0.15f);
            page.DrawText("Test Summary Report", margin, y);
            y -= 28;

            page.SetFont("Helvetica", 11);
            page.SetColor(0.4f, 0.4f, 0.45f);
            page.DrawText($"Project: {project.Name}", margin, y);
            y -= 16;
            page.DrawText($"Generated: {DateTime.Now:MMMM d, yyyy \u00B7 h:mm tt}", margin, y);
            y -= 8;

            // Separator
            y -= 12;
            page.SetColor(0.85f, 0.85f, 0.88f);
            page.DrawLine(margin, y, margin + contentWidth, y, 1);
            y -= 24;

            // Summary metrics
            int totalPlans = plans.Count;
            var casesByPlan = allCases.ToLookup(tc => tc.TestPlanId);
            var relevantCases = plans.SelectMany(p => casesByPlan[p.Id]).ToList();
            int totalCases = relevantCases.Count;
            int passed = 0, failed = 0, blocked = 0, skipped = 0, notRun = 0;
            foreach (var c in relevantCases)
            {
                switch (c.Status)
                {
                    case TestCaseStatus.Passed: passed++; break;
                    case TestCaseStatus.Failed: failed++; break;
                    case TestCaseStatus.Blocked: blocked++; break;
                    case TestCaseStatus.Skipped: skipped++; break;
                    case TestCaseStatus.NotRun: notRun++; break;
                }
            }
            double passRate = totalCases > 0 ? (double)passed / totalCases * 100 : 0;
            int totalExecs = allExecs.Count;

            page.SetFont("Helvetica-Bold", 14);
            page.SetColor(0.1f, 0.1f, 0.15f);
            page.DrawText("Overview", margin, y);
            y -= 24;

            // Metric boxes
            y = DrawMetricRow(page, margin, y, [
                ("Test Plans", totalPlans.ToString()),
                ("Test Cases", totalCases.ToString()),
                ("Executions", totalExecs.ToString()),
                ("Pass Rate", $"{passRate:F1}%")
            ]);
            y -= 20;

            // Status breakdown header
            page.SetFont("Helvetica-Bold", 14);
            page.SetColor(0.1f, 0.1f, 0.15f);
            page.DrawText("Status Breakdown", margin, y);
            y -= 24;

            // Status table
            y = DrawStatusTable(page, margin, y, contentWidth,
                passed, failed, blocked, skipped, notRun, totalCases);
            y -= 28;

            // â”€â”€ Test Plans detail â”€â”€
            page.SetFont("Helvetica-Bold", 14);
            page.SetColor(0.1f, 0.1f, 0.15f);
            page.DrawText("Test Plans", margin, y);
            y -= 20;

            foreach (var plan in plans.OrderByDescending(p => p.CreatedAt))
            {
                var casesInPlan = casesByPlan[plan.Id].ToList();
                int planPassed = casesInPlan.Count(c => c.Status == TestCaseStatus.Passed);
                int planFailed = casesInPlan.Count(c => c.Status == TestCaseStatus.Failed);
                double planRate = casesInPlan.Count > 0 ? (double)planPassed / casesInPlan.Count * 100 : 0;

                if (y < 120)
                {
                    page = pdf.AddPage();
                    y = 760;
                }

                // Plan header
                page.SetFillColor(0.96f, 0.96f, 0.98f);
                page.DrawRect(margin, y - 4, contentWidth, 22);

                page.SetFont("Helvetica-Bold", 11);
                page.SetColor(0.35f, 0.25f, 0.75f);
                page.DrawText(plan.TestPlanId, margin + 8, y);

                page.SetFont("Helvetica-Bold", 11);
                page.SetColor(0.15f, 0.15f, 0.2f);
                page.DrawText(plan.Name, margin + 60, y);

                page.SetFont("Helvetica", 10);
                page.SetColor(0.4f, 0.4f, 0.45f);
                page.DrawTextRight($"{casesInPlan.Count} cases Â· {planRate:F0}% pass", margin + contentWidth - 8, y);
                y -= 22;

                // Test cases in this plan
                foreach (var tc in casesInPlan.OrderBy(c => c.TestCaseId))
                {
                    if (y < 80)
                    {
                        page = pdf.AddPage();
                        y = 760;
                    }

                    page.SetFont("Courier", 9);
                    page.SetColor(0.35f, 0.25f, 0.75f);
                    page.DrawText(tc.TestCaseId, margin + 12, y);

                    page.SetFont("Helvetica", 9);
                    page.SetColor(0.2f, 0.2f, 0.25f);
                    string truncTitle = tc.Title.Length > 60 ? tc.Title[..57] + "..." : tc.Title;
                    page.DrawText(truncTitle, margin + 65, y);

                    var (statusLabel, sr, sg, sb) = GetStatusDisplay(tc.Status);
                    page.SetFont("Helvetica-Bold", 8);
                    page.SetColor(sr, sg, sb);
                    page.DrawTextRight(statusLabel, margin + contentWidth - 8, y);

                    y -= 16;
                }

                y -= 10;
            }

            // â”€â”€ Execution History (last 30) â”€â”€
            var recentExecs = allExecs.OrderByDescending(e => e.ExecutedAt).Take(30).ToList();
            if (recentExecs.Count > 0)
            {
                if (y < 200)
                {
                    page = pdf.AddPage();
                    y = 760;
                }

                page.SetFont("Helvetica-Bold", 14);
                page.SetColor(0.1f, 0.1f, 0.15f);
                page.DrawText("Recent Executions", margin, y);
                y -= 20;

                // Table header
                page.SetFillColor(0.92f, 0.92f, 0.95f);
                page.DrawRect(margin, y - 3, contentWidth, 18);
                page.SetFont("Helvetica-Bold", 8);
                page.SetColor(0.3f, 0.3f, 0.35f);
                page.DrawText("EXECUTION", margin + 6, y);
                page.DrawText("TEST CASE", margin + 80, y);
                page.DrawText("RESULT", margin + 300, y);
                page.DrawText("DATE", margin + 380, y);
                y -= 20;

                var caseLookup = allCases.ToDictionary(c => c.Id);

                foreach (var exec in recentExecs)
                {
                    if (y < 60)
                    {
                        page = pdf.AddPage();
                        y = 760;
                    }

                    caseLookup.TryGetValue(exec.TestCaseId, out var tc);

                    page.SetFont("Courier", 8);
                    page.SetColor(0.3f, 0.3f, 0.35f);
                    page.DrawText(exec.ExecutionId, margin + 6, y);

                    page.SetFont("Helvetica", 8);
                    page.SetColor(0.2f, 0.2f, 0.25f);
                    string tcLabel = tc != null
                        ? $"{tc.TestCaseId} Â· {(tc.Title.Length > 30 ? tc.Title[..27] + "..." : tc.Title)}"
                        : "Deleted";
                    page.DrawText(tcLabel, margin + 80, y);

                    var (statusLabel, sr, sg, sb) = GetStatusDisplay(exec.Result);
                    page.SetFont("Helvetica-Bold", 8);
                    page.SetColor(sr, sg, sb);
                    page.DrawText(statusLabel, margin + 300, y);

                    page.SetFont("Helvetica", 8);
                    page.SetColor(0.4f, 0.4f, 0.45f);
                    page.DrawText(exec.ExecutedAt.ToString("MMM d, yyyy HH:mm"), margin + 380, y);

                    y -= 15;
                }
            }

            // â”€â”€ Criticality Assessment â”€â”€
            if (!string.IsNullOrWhiteSpace(criticalityAssessment))
            {
                if (y < 300)
                {
                    page = pdf.AddPage();
                    y = 760;
                }

                // Section separator
                y -= 8;
                page.SetColor(0.85f, 0.85f, 0.88f);
                page.DrawLine(margin, y, margin + contentWidth, y, 1);
                y -= 24;

                // Section title
                page.SetFont("Helvetica-Bold", 14);
                page.SetColor(0.1f, 0.1f, 0.15f);
                page.DrawText("Criticality Assessment", margin, y);
                y -= 8;

                // Subtitle
                page.SetFont("Helvetica", 9);
                page.SetColor(0.45f, 0.45f, 0.5f);
                y -= 14;
                page.DrawText("AI-generated analysis based on project data, test cases, and execution results", margin, y);
                y -= 20;

                // Priority failure breakdown
                var failedCases = relevantCases.Where(c => c.Status == TestCaseStatus.Failed).ToList();
                int blockerFailed = 0, majorFailed = 0, mediumFailed = 0, lowFailed = 0;
                foreach (var c in failedCases)
                {
                    switch (c.Priority)
                    {
                        case TestCasePriority.Blocker: blockerFailed++; break;
                        case TestCasePriority.Major: majorFailed++; break;
                        case TestCasePriority.Medium: mediumFailed++; break;
                        case TestCasePriority.Low: lowFailed++; break;
                    }
                }
                int totalFailed = failedCases.Count;

                page.SetFont("Helvetica-Bold", 11);
                page.SetColor(0.15f, 0.15f, 0.2f);
                page.DrawText("Failures by Priority", margin, y);
                y -= 22;

                (string label, int count, float r, float g, float b)[] priorityRows =
                [
                    ("Blocker", blockerFailed, 0.86f, 0.15f, 0.15f),
                    ("Major",   majorFailed,   0.98f, 0.45f, 0.09f),
                    ("Medium",  mediumFailed,  0.98f, 0.75f, 0.14f),
                    ("Low",     lowFailed,     0.42f, 0.45f, 0.50f)
                ];

                foreach (var (label, count, r, g, b) in priorityRows)
                {
                    double pct = totalFailed > 0 ? (double)count / totalFailed * 100 : 0;

                    page.SetFillColor(0.94f, 0.94f, 0.96f);
                    page.DrawRect(margin + 80, y - 2, contentWidth - 150, 12);

                    if (pct > 0)
                    {
                        float barWidth = (float)((contentWidth - 150) * pct / 100);
                        page.SetFillColor(r, g, b);
                        page.DrawRect(margin + 80, y - 2, Math.Max(barWidth, 2), 12);
                    }

                    page.SetFont("Helvetica-Bold", 10);
                    page.SetColor(r, g, b);
                    page.DrawText(label, margin + 4, y);

                    page.SetFont("Helvetica", 10);
                    page.SetColor(0.2f, 0.2f, 0.25f);
                    page.DrawTextRight(count.ToString(), margin + contentWidth - 4, y);

                    y -= 20;
                }

                y -= 16;

                // AI Analysis text
                page.SetFont("Helvetica-Bold", 11);
                page.SetColor(0.15f, 0.15f, 0.2f);
                page.DrawText("AI Analysis", margin, y);
                y -= 6;

                // Thin accent line
                page.SetColor(0.65f, 0.55f, 0.98f);
                page.DrawLine(margin, y, margin + 60, y, 2);
                y -= 16;

                // Render the assessment text with word wrapping
                page.SetFont("Helvetica", 9);
                float lineHeight = 13;
                float maxLineWidth = contentWidth;

                foreach (var rawLine in criticalityAssessment.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');

                    // Detect markdown headings
                    if (line.StartsWith("## "))
                    {
                        y -= 6;
                        if (y < 60) { page = pdf.AddPage(); y = 760; }
                        page.SetFont("Helvetica-Bold", 11);
                        page.SetColor(0.15f, 0.15f, 0.2f);
                        page.DrawText(line[3..], margin, y);
                        y -= 18;
                        page.SetFont("Helvetica", 9);
                        page.SetColor(0.2f, 0.2f, 0.25f);
                        continue;
                    }

                    if (line.StartsWith("# "))
                    {
                        y -= 8;
                        if (y < 60) { page = pdf.AddPage(); y = 760; }
                        page.SetFont("Helvetica-Bold", 13);
                        page.SetColor(0.1f, 0.1f, 0.15f);
                        page.DrawText(line[2..], margin, y);
                        y -= 20;
                        page.SetFont("Helvetica", 9);
                        page.SetColor(0.2f, 0.2f, 0.25f);
                        continue;
                    }

                    // Bold lines (e.g. **text**)
                    bool isBold = line.StartsWith("**") && line.EndsWith("**") && line.Length > 4;
                    if (isBold)
                    {
                        page.SetFont("Helvetica-Bold", 9);
                        line = line[2..^2];
                    }
                    else
                    {
                        // Bullet points
                        bool isBullet = line.StartsWith("- ") || line.StartsWith("* ");
                        if (isBullet)
                            line = "\u2022 " + line[2..];

                        page.SetFont("Helvetica", 9);
                    }

                    page.SetColor(0.2f, 0.2f, 0.25f);

                    // Empty line â†’ small vertical gap
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        y -= 8;
                        continue;
                    }

                    // Word-wrap
                    var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var currentLine = new StringBuilder();
                    float charWidth = page.EstimateCharWidth();

                    foreach (var word in words)
                    {
                        float projectedWidth = (currentLine.Length + word.Length + 1) * charWidth;
                        if (currentLine.Length > 0 && projectedWidth > maxLineWidth)
                        {
                            if (y < 60) { page = pdf.AddPage(); y = 760; }
                            page.DrawText(currentLine.ToString(), margin, y);
                            y -= lineHeight;
                            currentLine.Clear();
                        }

                        if (currentLine.Length > 0) currentLine.Append(' ');
                        currentLine.Append(word);
                    }

                    if (currentLine.Length > 0)
                    {
                        if (y < 60) { page = pdf.AddPage(); y = 760; }
                        page.DrawText(currentLine.ToString(), margin, y);
                        y -= lineHeight;
                    }

                    if (isBold)
                        page.SetFont("Helvetica", 9);
                }
            }

            // Footer on last page
            page.SetFont("Helvetica", 8);
            page.SetColor(0.6f, 0.6f, 0.65f);
            page.DrawText($"QAssistant Â· {project.Name} Â· {DateTime.Now:yyyy-MM-dd}", margin, 30);

            return pdf.Build();
        }

        private static float DrawMetricRow(PdfPage page, float x, float y, (string label, string value)[] metrics)
        {
            float boxWidth = 110;
            float gap = 10;

            foreach (var (label, value) in metrics)
            {
                page.SetFillColor(0.96f, 0.96f, 0.98f);
                page.DrawRect(x, y - 8, boxWidth, 42);

                page.SetFont("Helvetica-Bold", 18);
                page.SetColor(0.15f, 0.15f, 0.2f);
                page.DrawText(value, x + 10, y + 14);

                page.SetFont("Helvetica", 9);
                page.SetColor(0.45f, 0.45f, 0.5f);
                page.DrawText(label, x + 10, y - 2);

                x += boxWidth + gap;
            }

            return y - 50;
        }

        private static float DrawStatusTable(PdfPage page, float x, float y, float width,
            int passed, int failed, int blocked, int skipped, int notRun, int total)
        {
            (string label, int count, float r, float g, float b)[] rows =
            [
                ("Passed", passed, 0.13f, 0.58f, 0.42f),
                ("Failed", failed, 0.85f, 0.24f, 0.24f),
                ("Blocked", blocked, 0.8f, 0.55f, 0.1f),
                ("Skipped", skipped, 0.42f, 0.45f, 0.5f),
                ("Not Run", notRun, 0.55f, 0.55f, 0.6f)
            ];

            foreach (var (label, count, r, g, b) in rows)
            {
                double pct = total > 0 ? (double)count / total * 100 : 0;

                // Bar background
                page.SetFillColor(0.94f, 0.94f, 0.96f);
                page.DrawRect(x + 100, y - 2, width - 170, 12);

                // Bar fill
                if (pct > 0)
                {
                    float barWidth = (float)((width - 170) * pct / 100);
                    page.SetFillColor(r, g, b);
                    page.DrawRect(x + 100, y - 2, Math.Max(barWidth, 2), 12);
                }

                page.SetFont("Helvetica", 10);
                page.SetColor(r, g, b);
                page.DrawText(label, x + 4, y);

                page.SetFont("Helvetica-Bold", 10);
                page.SetColor(0.2f, 0.2f, 0.25f);
                page.DrawTextRight($"{count}  ({pct:F1}%)", x + width - 4, y);

                y -= 20;
            }

            return y;
        }

        private static (string label, float r, float g, float b) GetStatusDisplay(TestCaseStatus status) => status switch
        {
            TestCaseStatus.Passed => ("PASSED", 0.13f, 0.58f, 0.42f),
            TestCaseStatus.Failed => ("FAILED", 0.85f, 0.24f, 0.24f),
            TestCaseStatus.Blocked => ("BLOCKED", 0.8f, 0.55f, 0.1f),
            TestCaseStatus.Skipped => ("SKIPPED", 0.42f, 0.45f, 0.5f),
            _ => ("NOT RUN", 0.55f, 0.55f, 0.6f)
        };

        // â”€â”€ Minimal PDF Builder â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Generates a valid PDF 1.4 document without external libraries.
        // Uses a two-pass approach: build all objects in memory, then
        // write them sequentially with correct byte offsets.

        private sealed class PdfBuilder
        {
            private readonly List<PdfPage> _pages = [];

            public PdfPage AddPage()
            {
                var page = new PdfPage();
                _pages.Add(page);
                return page;
            }

            public byte[] Build()
            {
                // Phase 1 â€“ Assign object numbers and build byte representations.
                //
                // Fixed objects:
                //   1  Catalog
                //   2  Pages  (needs page obj refs â†’ built last)
                //   3  Font Helvetica
                //   4  Font Helvetica-Bold
                //   5  Font Courier
                //
                // Per page (starting at obj 6):
                //   N    Content stream
                //   N+1  Page dictionary

                var objects = new List<byte[]?>
                {
                    null, // index 0 â†’ obj 1 (Catalog) â€“ filled below
                    null, // index 1 â†’ obj 2 (Pages)   â€“ filled after pages
                    Encoding.UTF8.GetBytes("3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n"),
                    Encoding.UTF8.GetBytes("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n"),
                    Encoding.UTF8.GetBytes("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>\nendobj\n"),
                };

                // Catalog (obj 1)
                objects[0] = Encoding.UTF8.GetBytes("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                var pageObjNumbers = new List<int>();

                foreach (var page in _pages)
                {
                    int contentObjNum = objects.Count + 1; // 1-based
                    var streamContent = Encoding.UTF8.GetBytes(page.GetStream());
                    var contentObj = Encoding.UTF8.GetBytes(
                        $"{contentObjNum} 0 obj\n<< /Length {streamContent.Length} >>\nstream\n");
                    var contentEnd = Encoding.UTF8.GetBytes("\nendstream\nendobj\n");

                    // Combine into one byte array
                    var full = new byte[contentObj.Length + streamContent.Length + contentEnd.Length];
                    Buffer.BlockCopy(contentObj, 0, full, 0, contentObj.Length);
                    Buffer.BlockCopy(streamContent, 0, full, contentObj.Length, streamContent.Length);
                    Buffer.BlockCopy(contentEnd, 0, full, contentObj.Length + streamContent.Length, contentEnd.Length);
                    objects.Add(full);

                    int pageObjNum = objects.Count + 1;
                    pageObjNumbers.Add(pageObjNum);
                    objects.Add(Encoding.UTF8.GetBytes(
                        $"{pageObjNum} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] " +
                        $"/Contents {contentObjNum} 0 R " +
                        $"/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> >>\nendobj\n"));
                }

                // Pages (obj 2)
                objects[1] = Encoding.UTF8.GetBytes(
                    "2 0 obj\n<< /Type /Pages /Kids [" +
                    string.Join(" ", pageObjNumbers.Select(n => $"{n} 0 R")) +
                    $"] /Count {_pages.Count} >>\nendobj\n");

                // Phase 2 â€“ Write sequentially, recording byte offsets.
                using var ms = new MemoryStream();

                // Header
                var header = "%PDF-1.4\n%\xe2\xe3\xcf\xd3\n"u8;
                ms.Write(header);

                var offsets = new int[objects.Count];
                for (int i = 0; i < objects.Count; i++)
                {
                    offsets[i] = (int)ms.Position;
                    ms.Write(objects[i]!);
                }

                // Cross-reference table
                long xrefOffset = ms.Position;

                var xref = new StringBuilder();
                xref.Append("xref\n");
                xref.Append(CultureInfo.InvariantCulture, $"0 {objects.Count + 1}\n");
                xref.Append("0000000000 65535 f \n");
                foreach (var off in offsets)
                    xref.Append(CultureInfo.InvariantCulture, $"{off:D10} 00000 n \n");

                xref.Append("trailer\n");
                xref.Append(CultureInfo.InvariantCulture, $"<< /Size {objects.Count + 1} /Root 1 0 R >>\n");
                xref.Append("startxref\n");
                xref.Append(CultureInfo.InvariantCulture, $"{xrefOffset}\n");
                xref.Append("%%EOF\n");

                ms.Write(Encoding.UTF8.GetBytes(xref.ToString()));

                return ms.ToArray();
            }
        }

        internal sealed class PdfPage
        {
            private readonly StringBuilder _stream = new();
            private string _currentFont = "/F1";
            private float _currentSize = 12;

            // Unicode â†’ WinAnsi byte for the special 0x80â€“0x9F range
            private static readonly Dictionary<char, byte> s_winAnsiSpecial = new()
            {
                { '\u20AC', 0x80 }, // â‚¬
                { '\u201A', 0x82 }, // â€š
                { '\u0192', 0x83 }, // Æ’
                { '\u201E', 0x84 }, // â€ž
                { '\u2026', 0x85 }, // â€¦
                { '\u2020', 0x86 }, // â€ 
                { '\u2021', 0x87 }, // â€¡
                { '\u02C6', 0x88 }, // Ë†
                { '\u2030', 0x89 }, // â€°
                { '\u0160', 0x8A }, // Å 
                { '\u2039', 0x8B }, // â€¹
                { '\u0152', 0x8C }, // Å’
                { '\u017D', 0x8E }, // Å½
                { '\u2018', 0x91 }, // '
                { '\u2019', 0x92 }, // '
                { '\u201C', 0x93 }, // "
                { '\u201D', 0x94 }, // "
                { '\u2022', 0x95 }, // â€¢
                { '\u2013', 0x96 }, // â€“
                { '\u2014', 0x97 }, // â€”
                { '\u02DC', 0x98 }, // Ëœ
                { '\u2122', 0x99 }, // â„¢
                { '\u0161', 0x9A }, // Å¡
                { '\u203A', 0x9B }, // â€º
                { '\u0153', 0x9C }, // Å“
                { '\u017E', 0x9E }, // Å¾
                { '\u0178', 0x9F }, // Å¸
            };

            // Fallback ASCII substitutions for characters outside WinAnsi
            private static readonly Dictionary<char, string> s_fallbacks = new()
            {
                { '\u2192', "->" },  // â†’
                { '\u2190', "<-" },  // â†
                { '\u2194', "<->" }, // â†”
                { '\u2191', "^" },   // â†‘
                { '\u2193', "v" },   // â†“
                { '\u2713', "+" },   // âœ“
                { '\u2714', "+" },   // âœ”
                { '\u2716', "x" },   // âœ–
                { '\u2717', "x" },   // âœ—
                { '\u2500', "-" },   // â”€
                { '\u2502', "|" },   // â”‚
                { '\u25A0', "#" },   // â– 
                { '\u25CF', "*" },   // â—
                { '\u2605', "*" },   // â˜…
                { '\u2610', "[ ]" }, // â˜
                { '\u2611', "[x]" }, // â˜‘
                { '\u2612', "[x]" }, // â˜’
                { '\u2003', " " },   // em space
                { '\u2002', " " },   // en space
                { '\u00A0', " " },   // non-breaking space â†’ regular space
            };

            public void SetFont(string fontName, float size)
            {
                _currentFont = fontName switch
                {
                    "Helvetica-Bold" => "/F2",
                    "Courier" => "/F3",
                    _ => "/F1"
                };
                _currentSize = size;
            }

            public void SetColor(float r, float g, float b)
            {
                _stream.Append(CultureInfo.InvariantCulture, $"{r:F3} {g:F3} {b:F3} rg\n");
                _stream.Append(CultureInfo.InvariantCulture, $"{r:F3} {g:F3} {b:F3} RG\n");
            }

            public void SetFillColor(float r, float g, float b)
            {
                _stream.Append(CultureInfo.InvariantCulture, $"{r:F3} {g:F3} {b:F3} rg\n");
            }

            public void DrawText(string text, float x, float y)
            {
                var encoded = EncodeWinAnsi(text);
                var hex = ToHexString(encoded);
                _stream.Append(CultureInfo.InvariantCulture, $"BT {_currentFont} {_currentSize:F1} Tf {x:F1} {y:F1} Td {hex} Tj ET\n");
            }

            public void DrawTextRight(string text, float rightX, float y)
            {
                var encoded = EncodeWinAnsi(text);
                float approxWidth = EstimateEncodedWidth(encoded.Length);
                var hex = ToHexString(encoded);
                _stream.Append(CultureInfo.InvariantCulture, $"BT {_currentFont} {_currentSize:F1} Tf {rightX - approxWidth:F1} {y:F1} Td {hex} Tj ET\n");
            }

            public void DrawLine(float x1, float y1, float x2, float y2, float width)
            {
                _stream.Append(CultureInfo.InvariantCulture, $"{width:F1} w {x1:F1} {y1:F1} m {x2:F1} {y2:F1} l S\n");
            }

            public void DrawRect(float x, float y, float w, float h)
            {
                _stream.Append(CultureInfo.InvariantCulture, $"{x:F1} {y:F1} {w:F1} {h:F1} re f\n");
            }

            public string GetStream() => _stream.ToString();

            /// <summary>
            /// Converts a Unicode string to a Windows-1252 byte sequence.
            /// Characters in the standard ASCII/Latin-1 ranges map directly.
            /// Characters in the WinAnsi special range (0x80â€“0x9F) are looked up.
            /// Everything else is substituted with an ASCII fallback or '?'.
            /// </summary>
            private static byte[] EncodeWinAnsi(string text)
            {
                var bytes = new List<byte>(text.Length);
                foreach (char c in text)
                {
                    // ASCII printable + standard controls (space..tilde)
                    if (c >= 0x20 && c <= 0x7E)
                    {
                        bytes.Add((byte)c);
                    }
                    // Latin-1 Supplement (U+00A0â€“U+00FF maps 1:1 in WinAnsi)
                    else if (c >= 0xA0 && c <= 0xFF)
                    {
                        bytes.Add((byte)c);
                    }
                    // WinAnsi special range (smart quotes, em dash, euro, etc.)
                    else if (s_winAnsiSpecial.TryGetValue(c, out byte winByte))
                    {
                        bytes.Add(winByte);
                    }
                    // Known fallback substitutions
                    else if (s_fallbacks.TryGetValue(c, out string? sub))
                    {
                        foreach (char sc in sub)
                        {
                            if (sc >= 0x20 && sc <= 0x7E)
                                bytes.Add((byte)sc);
                            else if (sc >= 0xA0 && sc <= 0xFF)
                                bytes.Add((byte)sc);
                        }
                    }
                    // Tab â†’ space
                    else if (c == '\t')
                    {
                        bytes.Add((byte)' ');
                    }
                    // Newlines â†’ space (PDF text operators don't handle line breaks)
                    else if (c == '\n' || c == '\r')
                    {
                        bytes.Add((byte)' ');
                    }
                    // Unknown â†’ ?
                    else
                    {
                        bytes.Add((byte)'?');
                    }
                }
                return bytes.ToArray();
            }

            /// <summary>
            /// Formats a WinAnsi byte array as a PDF hex string: &lt;4865â€¦&gt;
            /// This notation is binary-safe and avoids all escaping issues.
            /// </summary>
            private static string ToHexString(byte[] data)
            {
                var sb = new StringBuilder(data.Length * 2 + 2);
                sb.Append('<');
                foreach (byte b in data)
                    sb.Append(b.ToString("X2"));
                sb.Append('>');
                return sb.ToString();
            }

            private float EstimateEncodedWidth(int glyphCount)
            {
                float avgWidth = _currentFont switch
                {
                    "/F3" => 0.60f, // Courier (monospaced)
                    "/F2" => 0.52f, // Helvetica-Bold
                    _ => 0.48f      // Helvetica
                };
                return glyphCount * _currentSize * avgWidth;
            }

            internal float EstimateCharWidth()
            {
                return _currentFont switch
                {
                    "/F3" => 0.60f * _currentSize,
                    "/F2" => 0.52f * _currentSize,
                    _ => 0.48f * _currentSize
                };
            }
        }
    }
}
