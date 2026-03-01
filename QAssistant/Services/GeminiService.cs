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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    public class GeminiService(string apiKey) : IDisposable
    {
        private readonly HttpClient _client = CreateClient(apiKey);
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
        private const string PrimaryModel = "models/gemini-2.5-flash";
        private const string FallbackModel = "models/gemini-2.0-flash";

        public void Dispose() => _client.Dispose();

        private static HttpClient CreateClient(string apiKey)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
            return client;
        }

        /// <summary>
        /// Sanitizer variant for test-case generation prompts.
        /// Preserves newlines and allows a larger max length so detailed
        /// Jira descriptions (lists, steps) are retained for the model.
        /// </summary>
        private static string SanitizeToonValueForTestGen(string? value, int maxLength = 2000)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var s = value.Length > maxLength ? value[..maxLength] + "..." : value;

            // Preserve newlines for test-step structure but normalize CRLF
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');

            // Replace TOON structural delimiters with safe visual equivalents
            s = s.Replace('{', '(').Replace('}', ')');
            s = s.Replace('[', '(').Replace(']', ')');

            // Neutralise directive prefix
            s = s.Replace("@", "(at)");

            // Break TOON section separators
            s = s.Replace("---", "- - -");

            return s;
        }

        /// <summary>
        /// Sanitizes a free-text value before embedding it in a TOON prompt.
        /// Neutralises characters that could break TOON structural boundaries
        /// or inject directives, while preserving human-readable content.
        /// </summary>
        private static string SanitizeToonValue(string? value, int maxLength = 500)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var s = value.Length > maxLength ? value[..maxLength] + "..." : value;

            // Collapse newlines to spaces – prevents line-based directive injection
            s = s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

            // Replace TOON structural delimiters with safe visual equivalents
            s = s.Replace('{', '(').Replace('}', ')');
            s = s.Replace('[', '(').Replace(']', ')');

            // Neutralise directive prefix at the start and after spaces
            s = s.Replace("@", "(at)");

            // Break TOON section separators
            s = s.Replace("---", "- - -");

            return s;
        }

        /// <summary>
        /// Appends a compact QA context block to the prompt so Gemini understands
        /// the project landscape from a QA Engineer perspective: active environment,
        /// test coverage snapshot, checklist categories, and test data domains.
        /// </summary>
        private static void AppendQaContext(StringBuilder sb, Project? project)
        {
            if (project == null) return;

            sb.AppendLine("qa_context{");
            sb.AppendLine($" project:{SanitizeToonValue(project.Name, 200)}");

            if (!string.IsNullOrWhiteSpace(project.Description))
                sb.AppendLine($" project_desc:{SanitizeToonValue(project.Description, 300)}");

            // Active environment
            var activeEnv = project.Environments.Find(e => e.IsDefault)
                         ?? (project.Environments.Count > 0 ? project.Environments[0] : null);
            if (activeEnv != null)
            {
                sb.AppendLine($" active_env:{SanitizeToonValue(activeEnv.Name, 100)}");
                sb.AppendLine($" env_type:{activeEnv.Type}");
                if (!string.IsNullOrWhiteSpace(activeEnv.BaseUrl))
                    sb.AppendLine($" env_url:{SanitizeToonValue(activeEnv.BaseUrl, 200)}");
            }

            // Environment landscape
            if (project.Environments.Count > 0)
            {
                var envTypes = string.Join(",", project.Environments
                    .Select(e => $"{SanitizeToonValue(e.Name, 60)}({e.Type})"));
                sb.AppendLine($" environments:{envTypes}");
            }

            // Test coverage snapshot
            if (project.TestCases.Count > 0)
            {
                int passed = 0, failed = 0, blocked = 0, notRun = 0;
                foreach (var tc in project.TestCases)
                {
                    switch (tc.Status)
                    {
                        case TestCaseStatus.Passed: passed++; break;
                        case TestCaseStatus.Failed: failed++; break;
                        case TestCaseStatus.Blocked: blocked++; break;
                        case TestCaseStatus.NotRun: notRun++; break;
                    }
                }
                sb.AppendLine($" test_coverage:total={project.TestCases.Count},passed={passed},failed={failed},blocked={blocked},not_run={notRun}");
            }

            // Checklist categories being tracked
            if (project.Checklists.Count > 0)
            {
                var categories = project.Checklists
                    .Where(c => !string.IsNullOrWhiteSpace(c.Category))
                    .Select(c => SanitizeToonValue(c.Category, 80))
                    .Distinct()
                    .ToList();
                if (categories.Count > 0)
                    sb.AppendLine($" checklist_areas:{string.Join(",", categories)}");
            }

            // Test data domains
            if (project.TestDataGroups.Count > 0)
            {
                var dataDomains = project.TestDataGroups
                    .Where(g => !string.IsNullOrWhiteSpace(g.Category))
                    .Select(g => SanitizeToonValue(g.Category, 60))
                    .Distinct()
                    .ToList();
                if (dataDomains.Count > 0)
                    sb.AppendLine($" test_data_domains:{string.Join(",", dataDomains)}");
            }

            sb.AppendLine("}");
            sb.AppendLine("---");

            // SAP Commerce domain context (appended only when enabled in Settings)
            var sapContext = SapCommerceContext.GetContextBlock();
            if (sapContext != null)
            {
                sb.AppendLine(sapContext);
                sb.AppendLine("---");
            }
        }

        /// <summary>
        /// Builds a Token-Oriented Object Notation (TOON) prompt for issue analysis.

        /// TOON uses compact key-value pairs and directive-style instructions to
        /// significantly reduce token consumption while preserving semantic quality.
        /// </summary>
        public static string BuildToonPrompt(ProjectTask task, IReadOnlyList<LinearComment>? comments = null, int attachedImageCount = 0, Project? project = null)
        {
            var sb = new StringBuilder();

            // Directives block — compact instruction set
            sb.AppendLine("@role:sr_qa_engineer");
            sb.AppendLine("@task:deep_issue_analysis");
            sb.AppendLine("@perspective:qa_engineer—focus on testability,reproducibility,regression_risk,environment_impact");
            sb.AppendLine("@out_fmt:md_sections[## Root Cause Analysis,## Impact Assessment,## Suggested Fix,## Prevention Recommendations]");
            sb.AppendLine("@rules:all_sections_required|multi_sentence|specific_actionable|infer_if_brief|no_skip|no_merge|consider_env_context|reference_test_coverage");
            sb.AppendLine("---");

            // QA project context
            AppendQaContext(sb, project);

            // Issue data in TOON object notation
            sb.AppendLine("issue{");
            sb.AppendLine($" t:{SanitizeToonValue(task.Title, 300)}");

            if (!string.IsNullOrEmpty(task.IssueIdentifier))
                sb.AppendLine($" id:{SanitizeToonValue(task.IssueIdentifier, 100)}");

            sb.AppendLine($" status:{task.Status}");
            sb.AppendLine($" priority:{task.Priority}");

            if (!string.IsNullOrEmpty(task.Assignee))
                sb.AppendLine($" assignee:{SanitizeToonValue(task.Assignee, 200)}");

            if (!string.IsNullOrEmpty(task.Labels))
                sb.AppendLine($" labels:{SanitizeToonValue(task.Labels, 200)}");

            if (task.DueDate.HasValue)
                sb.AppendLine($" due:{task.DueDate.Value:yyyy-MM-dd}");

            sb.AppendLine($" desc:{(string.IsNullOrWhiteSpace(task.Description) ? "(none—infer from title+metadata)" : SanitizeToonValue(task.Description))}");
            sb.AppendLine("}");

            // Comments in TOON array notation
            if (comments is { Count: > 0 })
            {
                sb.AppendLine("comments[");
                foreach (var c in comments)
                    sb.AppendLine($" {{author:{SanitizeToonValue(c.AuthorName, 200)},date:{c.CreatedAt:yyyy-MM-dd},body:{SanitizeToonValue(c.Body)}}}");
                sb.AppendLine("]");
            }

            if (attachedImageCount > 0)
                sb.AppendLine($"@media:{attachedImageCount}_image(s)_attached—analyze visual content for additional context (screenshots, error messages, UI state, logs)");

            return sb.ToString();
        }

        public async Task<List<(string, string[])>> ListModelsAsync()

        {
            var url = $"{BaseUrl}/models";
            var resp = await _client.GetAsync(url);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"ListModels failed ({(int)resp.StatusCode}): {json}");

            using var doc = JsonDocument.Parse(json);
            var list = new List<(string, string[])>();

            if (doc.RootElement.TryGetProperty("models", out var models))
            {
                foreach (var m in models.EnumerateArray())
                {
                    var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "<unknown>" : "<unknown>";

                    // The API may return supported generation method names under different property
                    // names depending on the server version. Prefer the newer property but fall back
                    // to older property name for compatibility.
                    string[] methods = Array.Empty<string>();
                    if (m.TryGetProperty("supportedGenerationMethods", out var sgm))
                    {
                        methods = sgm.EnumerateArray().Select(x => x.GetString() ?? string.Empty)
                                         .Where(x => !string.IsNullOrEmpty(x)).ToArray();
                    }
                    else if (m.TryGetProperty("supportedMethods", out var sm))
                    {
                        methods = sm.EnumerateArray().Select(x => x.GetString() ?? string.Empty)
                                        .Where(x => !string.IsNullOrEmpty(x)).ToArray();
                    }
                    list.Add((name, methods));
                }

                return list;
            }

            throw new Exception("ListModels returned unexpected response: " + json);
        }

        private static readonly string[] CandidateGenerateMethods = new[] { "generateContent", "generateMessage", "generateText" };

        private async Task<string?> FindModelSupportingGenerateContentAsync()
        {
            // Backwards-compatible wrapper that prefers known method names but falls back to any 'generate' containing method.
            var models = await ListModelsAsync();

            foreach (var method in CandidateGenerateMethods)
            {
                var found = models.FirstOrDefault(m => m.Item2.Any(s => string.Equals(s, method, StringComparison.OrdinalIgnoreCase)));
                if (!string.IsNullOrEmpty(found.Item1))
                    return found.Item1;
            }

            // Fallback: pick first model that exposes any method containing the word 'generate'
            var fallback = models.FirstOrDefault(m => m.Item2.Any(s => s.IndexOf("generate", StringComparison.OrdinalIgnoreCase) >= 0));
            return fallback.Item1;
        }

        /// <summary>
        /// Builds a TOON prompt for generating test cases from project documentation.
        /// </summary>
        public static string BuildTestCaseGenerationPrompt(IReadOnlyList<ProjectTask> tasks, string sourceName, Project? project = null, string? designDocContent = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine("@role:sr_qa_engineer");
            sb.AppendLine("@task:generate_test_cases");
            sb.AppendLine("@perspective:qa_engineer—generate tests a QA engineer would write for regression,smoke,functional,integration suites");
            sb.AppendLine("@source:" + sourceName);
            sb.AppendLine("@out_fmt:json_array[{testCaseId,title,preConditions,testSteps,testData,expectedResult,priority,sourceIssueId}]");
            sb.AppendLine("@out_rules:raw_json_only|no_markdown_wrap|no_code_block");
            sb.AppendLine("@rules:comprehensive|all_fields_required|specific_actionable|realistic_test_data|cover_positive_negative_edge|no_generic|env_aware|use_known_test_data_when_applicable");
            if (!string.IsNullOrWhiteSpace(designDocContent))
                sb.AppendLine("@extra_context:design_document_provided—use it to improve accuracy,coverage,and specificity of generated test cases");
            sb.AppendLine("---");

            // QA project context
            AppendQaContext(sb, project);

            // Design document context (optional, improves accuracy)
            if (!string.IsNullOrWhiteSpace(designDocContent))
            {
                sb.AppendLine("design_document{");
                sb.AppendLine(SanitizeToonValueForTestGen(designDocContent, 20000));
                sb.AppendLine("}");
                sb.AppendLine("---");
            }

            sb.AppendLine("field_spec{");
            sb.AppendLine(" testCaseId:sequential(TC-001,TC-002,...)");
            sb.AppendLine(" title:clear_descriptive");
            sb.AppendLine(" preConditions:state_before_execution");
            sb.AppendLine(" testSteps:numbered_step_by_step");
            sb.AppendLine(" testData:specific_values");
            sb.AppendLine(" expectedResult:pass_criteria");
            sb.AppendLine(" priority:one_of(Blocker,Major,Medium,Low)_based_on_issue_severity_and_impact");
            sb.AppendLine(" sourceIssueId:exact_id_of_the_source_issue_this_test_case_covers(IssueIdentifier_field_value)");
            sb.AppendLine("}");
            sb.AppendLine("---");

            sb.AppendLine("project_issues[");
            foreach (var task in tasks)
            {
                sb.Append($" {{id:{SanitizeToonValue(task.IssueIdentifier, 100)}");
                sb.Append($",title:{SanitizeToonValue(task.Title, 300)}");
                sb.Append($",status:{task.Status}");
                sb.Append($",priority:{task.Priority}");

                if (!string.IsNullOrWhiteSpace(task.Description))
                {
                    // Use a specialized sanitizer for test-case generation to keep
                    // multiline steps and longer descriptions intact.
                    sb.Append($",desc:{SanitizeToonValueForTestGen(task.Description, 2000)}");
                }

                if (!string.IsNullOrEmpty(task.IssueType))
                    sb.Append($",type:{SanitizeToonValue(task.IssueType, 100)}");

                if (!string.IsNullOrEmpty(task.Labels))
                    sb.Append($",labels:{SanitizeToonValue(task.Labels, 200)}");

                sb.AppendLine("}");
            }
            sb.AppendLine("]");

            return sb.ToString();
        }

        /// <summary>
        /// Parses Gemini's response into a list of <see cref="TestCase"/> objects.
        /// Handles JSON arrays that may be wrapped in markdown code blocks.
        /// </summary>
        public static List<TestCase> ParseTestCasesFromResponse(string response, TaskSource source, int maxTestCases = 200)
        {
            var json = response.Trim();

            // Strip markdown code block wrappers (```json ... ``` or ``` ... ```)
            if (json.StartsWith("```"))
            {
                var startIdx = json.IndexOf('\n');
                if (startIdx >= 0)
                {
                    startIdx++;
                    var endIdx = json.LastIndexOf("```");
                    if (endIdx > startIdx)
                        json = json[startIdx..endIdx].Trim();
                }
            }

            // Extract the first balanced JSON array from the response text. This
            // is more robust than IndexOf/LastIndexOf when the model returns
            // surrounding text or multiple arrays.
            var extracted = ExtractFirstJsonArray(json);
            if (string.IsNullOrEmpty(extracted))
                throw new JsonException("Could not locate a JSON array in the model response.");

            using var doc = JsonDocument.Parse(extracted);
            var testCases = new List<TestCase>();
            int counter = 1;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (testCases.Count >= maxTestCases)
                    break;

                const int MaxFieldLength = 10_000;

                var tc = new TestCase
                {
                    TestCaseId = Truncate(GetJsonString(element, "testCaseId") ?? $"TC-{counter:D3}", 50),
                    Title = Truncate(GetJsonString(element, "title") ?? $"Test Case {counter}", MaxFieldLength),
                    PreConditions = Truncate(GetJsonString(element, "preConditions") ?? "", MaxFieldLength),
                    TestSteps = Truncate(GetJsonString(element, "testSteps") ?? "", MaxFieldLength),
                    TestData = Truncate(GetJsonString(element, "testData") ?? "", MaxFieldLength),
                    ExpectedResult = Truncate(GetJsonString(element, "expectedResult") ?? "", MaxFieldLength),
                    Priority = ParsePriority(GetJsonString(element, "priority")),
                    SourceIssueId = Truncate(GetJsonString(element, "sourceIssueId") ?? "", 100),
                    Source = source,
                    GeneratedAt = DateTime.Now
                };
                testCases.Add(tc);
                counter++;
            }

            return testCases;
        }

        // Finds and returns the first balanced JSON array (including brackets)
        // from the input text, or null if none found. Handles quoted strings
        // and escaped characters so brackets inside strings are ignored.
        private static string? ExtractFirstJsonArray(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            int start = input.IndexOf('[');
            if (start < 0) return null;

            int depth = 0;
            bool inString = false;
            bool escape = false;

            for (int i = start; i < input.Length; i++)
            {
                char c = input[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return input[start..(i + 1)];
                    }
                }
            }

            return null;
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length > maxLength ? value[..maxLength] : value;

        private static string? GetJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
                return value.GetString();
            return null;
        }

        private static TestCasePriority ParsePriority(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return TestCasePriority.Medium;

            return value.Trim().ToLowerInvariant() switch
            {
                "blocker" => TestCasePriority.Blocker,
                "major" => TestCasePriority.Major,
                "medium" => TestCasePriority.Medium,
                "low" => TestCasePriority.Low,
                _ => TestCasePriority.Medium
            };
        }

        /// <summary>
        /// Builds a prompt for generating a Criticality Assessment based on the entire project overview.
        /// </summary>
        public static string BuildCriticalityAssessmentPrompt(
            IReadOnlyList<ProjectTask> tasks,
            IReadOnlyList<TestCase> testCases,
            IReadOnlyList<TestExecution> executions,
            IReadOnlyList<TestPlan> testPlans,
            Project? project = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine("@role:sr_qa_engineer");
            sb.AppendLine("@task:criticality_assessment");
            sb.AppendLine("@perspective:qa_engineer—assess release risk from QA standpoint considering environment health,test coverage gaps,checklist completion,blocker density");
            sb.AppendLine("@out_fmt:md_sections[## Failure Summary by Priority,## Overall Risk Level,## Key Areas of Concern,## Recommended Actions,## Release Readiness]");
            sb.AppendLine("@rules:concise|actionable|data_driven|risk_focused|all_sections_required|include_counts_per_priority(Blocker,Major,Medium,Low)|risk_level_one_of(Critical,High,Moderate,Low)|actions_ordered_by_severity|no_skip|no_merge|factor_env_coverage|factor_checklist_gaps");
            sb.AppendLine("---");

            // QA project context
            AppendQaContext(sb, project);

            // Failed test cases grouped by priority
            var failedCases = testCases.Where(tc => tc.Status == TestCaseStatus.Failed).ToList();
            int blockerFailed = 0, majorFailed = 0, mediumFailed = 0, lowFailed = 0;
            foreach (var tc in failedCases)
            {
                switch (tc.Priority)
                {
                    case TestCasePriority.Blocker: blockerFailed++; break;
                    case TestCasePriority.Major: majorFailed++; break;
                    case TestCasePriority.Medium: mediumFailed++; break;
                    case TestCasePriority.Low: lowFailed++; break;
                }
            }

            sb.AppendLine("failure_summary{");
            sb.AppendLine($" total_test_cases:{testCases.Count}");
            sb.AppendLine($" total_failed:{failedCases.Count}");
            sb.AppendLine($" blocker_failed:{blockerFailed}");
            sb.AppendLine($" major_failed:{majorFailed}");
            sb.AppendLine($" medium_failed:{mediumFailed}");
            sb.AppendLine($" low_failed:{lowFailed}");
            sb.AppendLine($" total_executions:{executions.Count}");
            sb.AppendLine($" total_test_plans:{testPlans.Count}");
            sb.AppendLine("}");

            // Project tasks overview
            if (tasks.Count > 0)
            {
                sb.AppendLine("project_tasks[");
                foreach (var task in tasks.Take(50))
                {
                    sb.Append($" {{id:{SanitizeToonValue(task.IssueIdentifier, 100)},title:{SanitizeToonValue(task.Title, 300)}");
                    sb.Append($",status:{task.Status},priority:{task.Priority}");
                    if (!string.IsNullOrEmpty(task.IssueType))
                        sb.Append($",type:{SanitizeToonValue(task.IssueType, 100)}");
                    sb.AppendLine("}");
                }
                sb.AppendLine("]");
            }

            // Failed test case details
            if (failedCases.Count > 0)
            {
                sb.AppendLine("failed_test_cases[");
                foreach (var tc in failedCases.Take(50))
                {
                    sb.Append($" {{id:{SanitizeToonValue(tc.TestCaseId, 100)},title:{SanitizeToonValue(tc.Title, 300)}");
                    sb.Append($",priority:{tc.Priority},source:{tc.Source}");
                    if (!string.IsNullOrWhiteSpace(tc.ActualResult))
                    {
                        sb.Append($",actual_result:{SanitizeToonValue(tc.ActualResult, 200)}");
                    }
                    sb.AppendLine("}");
                }
                sb.AppendLine("]");
            }

            // Execution results in TOON inline-object notation
            if (executions.Count > 0)
            {
                var resultGroups = executions.GroupBy(e => e.Result)
                    .Select(g => $"{g.Key}:{g.Count()}");
                sb.AppendLine($"exec_results{{{string.Join(",", resultGroups)}}}");
            }

            // Test plan details in TOON array notation
            if (testPlans.Count > 0)
            {
                var casesByPlan = testCases.ToLookup(tc => tc.TestPlanId);
                sb.AppendLine("test_plans[");
                foreach (var plan in testPlans.Take(50))
                {
                    var planCases = casesByPlan[plan.Id];
                    int planCaseCount = 0, planFailedCount = 0;
                    foreach (var tc in planCases)
                    {
                        planCaseCount++;
                        if (tc.Status == TestCaseStatus.Failed) planFailedCount++;
                    }
                    sb.AppendLine($" {{id:{SanitizeToonValue(plan.TestPlanId, 100)},name:{SanitizeToonValue(plan.Name, 200)},cases:{planCaseCount},failed:{planFailedCount},source:{plan.Source}}}");
                }
                sb.AppendLine("]");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds a TOON prompt that asks Gemini for actionable QA suggestions based on
        /// current test run status, pass rate per test plan, and failed test case impact.
        /// </summary>
        public static string BuildTestRunSuggestionsPrompt(
            IReadOnlyList<TestCase> testCases,
            IReadOnlyList<TestExecution> executions,
            IReadOnlyList<TestPlan> testPlans,
            Project? project = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine("@role:sr_qa_engineer");
            sb.AppendLine("@task:test_run_suggestions");
            sb.AppendLine("@perspective:qa_engineer—give specific,actionable QA gate and deployment suggestions based on test run results,pass rates per plan,and failed test case impact");
            sb.AppendLine("@out_fmt:md_sections[## Overall Status,## Deployment Readiness,## Key Risks,## Suggestions]");
            sb.AppendLine("@rules:concise|specific|data_driven|bold_decisions|deployment_verdict_prominent|reference_failing_areas|no_generic_advice|all_sections_required|suggestions_imperative_sentences_referencing_actual_data");
            sb.AppendLine("@example_output:Do not deploy to UAT — 3 blocker failures in the Checkout UI module|Retest Payment flow before promoting to staging — 2 major failures detected|UI regression suite is at 45% pass rate — address before UAT");
            sb.AppendLine("---");

            AppendQaContext(sb, project);

            // Overall pass rate
            int total = testCases.Count;
            int passed = testCases.Count(tc => tc.Status == TestCaseStatus.Passed);
            int failed = testCases.Count(tc => tc.Status == TestCaseStatus.Failed);
            int blocked = testCases.Count(tc => tc.Status == TestCaseStatus.Blocked);
            int skipped = testCases.Count(tc => tc.Status == TestCaseStatus.Skipped);
            int notRun = testCases.Count(tc => tc.Status == TestCaseStatus.NotRun);
            double passRate = total > 0 ? (double)passed / total * 100 : 0;

            sb.AppendLine("overall_stats{");
            sb.AppendLine($" total_cases:{total}");
            sb.AppendLine($" passed:{passed}");
            sb.AppendLine($" failed:{failed}");
            sb.AppendLine($" blocked:{blocked}");
            sb.AppendLine($" skipped:{skipped}");
            sb.AppendLine($" not_run:{notRun}");
            sb.AppendLine($" pass_rate:{passRate:F1}%");
            sb.AppendLine($" total_executions:{executions.Count}");
            sb.AppendLine("}");

            // Per-plan pass rate
            if (testPlans.Count > 0)
            {
                var casesByPlan = testCases.ToLookup(tc => tc.TestPlanId);
                sb.AppendLine("plan_results[");
                foreach (var plan in testPlans.Take(20))
                {
                    var planCases = casesByPlan[plan.Id].ToList();
                    int planTotal = planCases.Count;
                    int planPassed = planCases.Count(c => c.Status == TestCaseStatus.Passed);
                    int planFailed = planCases.Count(c => c.Status == TestCaseStatus.Failed);
                    int planBlocked = planCases.Count(c => c.Status == TestCaseStatus.Blocked);
                    double planRate = planTotal > 0 ? (double)planPassed / planTotal * 100 : 0;
                    sb.AppendLine($" {{name:{SanitizeToonValue(plan.Name, 200)},total:{planTotal},passed:{planPassed},failed:{planFailed},blocked:{planBlocked},pass_rate:{planRate:F1}%,source:{plan.Source}}}");
                }
                sb.AppendLine("]");
            }

            // Failed test case details with priority and impact area
            var failedCases = testCases.Where(tc => tc.Status == TestCaseStatus.Failed).ToList();
            if (failedCases.Count > 0)
            {
                sb.AppendLine("failed_cases[");
                foreach (var tc in failedCases.Take(50))
                {
                    sb.Append($" {{id:{SanitizeToonValue(tc.TestCaseId, 100)},title:{SanitizeToonValue(tc.Title, 300)},priority:{tc.Priority}");
                    if (tc.SapModule.HasValue)
                        sb.Append($",module:{tc.SapModule.Value}");
                    if (!string.IsNullOrWhiteSpace(tc.ActualResult))
                        sb.Append($",actual:{SanitizeToonValue(tc.ActualResult, 200)}");
                    if (!string.IsNullOrWhiteSpace(tc.SourceIssueId))
                        sb.Append($",issue:{SanitizeToonValue(tc.SourceIssueId, 60)}");
                    sb.AppendLine("}");
                }
                sb.AppendLine("]");
            }

            // Blocked test cases
            var blockedCases = testCases.Where(tc => tc.Status == TestCaseStatus.Blocked).ToList();
            if (blockedCases.Count > 0)
            {
                sb.AppendLine("blocked_cases[");
                foreach (var tc in blockedCases.Take(20))
                {
                    sb.Append($" {{title:{SanitizeToonValue(tc.Title, 200)},priority:{tc.Priority}");
                    if (tc.SapModule.HasValue)
                        sb.Append($",module:{tc.SapModule.Value}");
                    sb.AppendLine("}");
                }
                sb.AppendLine("]");
            }

            // Execution result breakdown
            if (executions.Count > 0)
            {
                var resultGroups = executions.GroupBy(e => e.Result)
                    .Select(g => $"{g.Key}:{g.Count()}");
                sb.AppendLine($"exec_results{{{string.Join(",", resultGroups)}}}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds a TOON prompt that asks Gemini to select a minimal smoke-test subset
        /// (up to 30 IDs) from the supplied candidate test cases.
        /// The response is expected to be a raw JSON array of test case ID strings.
        ///
        /// Token optimisation: a single <c>@schema</c> directive declares field and value
        /// abbreviations used in every entry of the <c>done[]</c> and <c>tc[]</c> blocks,
        /// yielding ≈ 70–80 chars saved per test case (≈ 2 000 tokens at 100 TCs).
        /// Key mappings:  t=title, p=priority, s=status, iss=source_issue_id.
        /// Priority tokens: B=Blocker, MAJ=Major, MED=Medium, L=Low.
        /// Status tokens:   F=Failed, P=Passed, BL=Blocked, SK=Skipped (NR=NotRun omitted as default).
        /// </summary>
        public static string BuildSmokeSubsetPrompt(
            IReadOnlyList<TestCase> candidates,
            IReadOnlyList<ProjectTask> doneTasks,
            Project? project = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine("@role:sr_qa_engineer");
            sb.AppendLine("@task:smoke_subset_selection");
            sb.AppendLine("@goal:minimal_tc_set_max_regression_coverage");
            sb.AppendLine("@out_fmt:json_array_of_strings");
            sb.AppendLine("@out_rules:raw_json_only|no_wrap|ids_only|max_30");
            sb.AppendLine("@sel_rules:prefer(B>MAJ>MED>L)|cover_distinct_areas|no_dupes|exact_ids");
            // Schema declaration — enables abbreviated keys/values in all data blocks below
            sb.AppendLine("@schema:t=title|p=priority(B=Blocker,MAJ=Major,MED=Medium,L=Low)|s=status(F=Failed,P=Passed,BL=Blocked,SK=Skipped)|iss=source_issue_id");
            sb.AppendLine("---");

            AppendQaContext(sb, project);

            if (doneTasks.Count > 0)
            {
                sb.AppendLine("done[");
                foreach (var task in doneTasks.Take(50))
                    sb.AppendLine($" {{id:{SanitizeToonValue(task.IssueIdentifier, 60)},t:{SanitizeToonValue(task.Title, 120)},p:{AbbreviateTaskPriority(task.Priority)}}}");
                sb.AppendLine("]");
            }

            // tc[] entries omit s: when status is NotRun (the default) and omit iss: when empty,
            // keeping each line as short as possible while preserving all selector-relevant signal.
            sb.AppendLine("tc[");
            foreach (var tc in candidates.Take(200))
            {
                sb.Append($" {{id:{SanitizeToonValue(tc.TestCaseId, 50)},t:{SanitizeToonValue(tc.Title, 100)},p:{AbbreviateTestCasePriority(tc.Priority)}");
                if (tc.Status != TestCaseStatus.NotRun)
                    sb.Append($",s:{AbbreviateStatus(tc.Status)}");
                if (!string.IsNullOrEmpty(tc.SourceIssueId))
                    sb.Append($",iss:{SanitizeToonValue(tc.SourceIssueId, 60)}");
                sb.AppendLine("}");
            }
            sb.AppendLine("]");

            return sb.ToString();
        }

        // ── TOON abbreviation helpers ─────────────────────────────────────────

        private static string AbbreviateTestCasePriority(TestCasePriority priority) => priority switch
        {
            TestCasePriority.Blocker => "B",
            TestCasePriority.Major   => "MAJ",
            TestCasePriority.Medium  => "MED",
            TestCasePriority.Low     => "L",
            _                        => "MED"
        };

        // Critical task priority maps to Blocker in QA domain severity terms
        private static string AbbreviateTaskPriority(TaskPriority priority) => priority switch
        {
            TaskPriority.Critical => "B",
            TaskPriority.High     => "MAJ",
            TaskPriority.Medium   => "MED",
            TaskPriority.Low      => "L",
            _                     => "MED"
        };

        private static string AbbreviateStatus(TestCaseStatus status) => status switch
        {
            TestCaseStatus.Failed  => "F",
            TestCaseStatus.Passed  => "P",
            TestCaseStatus.Blocked => "BL",
            TestCaseStatus.Skipped => "SK",
            _                      => "NR"
        };

        /// <summary>
        /// Parses the smoke subset response from Gemini into a list of test case ID strings.
        /// Handles optional markdown wrappers and returns an empty list on parse failure.
        /// </summary>
        public static List<string> ParseSmokeSubsetIds(string response)
        {
            var json = response.Trim();

            // Strip optional markdown code block wrapper
            if (json.StartsWith("```"))
            {
                var startIdx = json.IndexOf('\n');
                if (startIdx >= 0)
                {
                    startIdx++;
                    var endIdx = json.LastIndexOf("```");
                    if (endIdx > startIdx)
                        json = json[startIdx..endIdx].Trim();
                }
            }

            var extracted = ExtractFirstJsonArray(json);
            if (string.IsNullOrEmpty(extracted))
                return [];

            try
            {
                using var doc = JsonDocument.Parse(extracted);
                var ids = new List<string>();
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var id = element.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                            ids.Add(id.Trim());
                    }
                }
                return ids;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public async Task<string> AnalyzeIssueAsync(string prompt, IReadOnlyList<(string MimeType, string Base64Data)>? images = null, string? modelName = null)
        {
            var primaryModel = NormalizeModelPath(modelName ?? PrimaryModel);

            // If the caller explicitly passed the fallback model as primary, swap so there is
            // always a distinct second model to try.
            var fallbackModel = string.Equals(primaryModel, FallbackModel, StringComparison.OrdinalIgnoreCase)
                ? PrimaryModel
                : FallbackModel;

            // Try the primary model first
            try
            {
                return await SendGenerateRequestAsync(prompt, primaryModel, images);
            }
            catch (GeminiRateLimitException firstEx)
            {
                Debug.WriteLine($"Rate limit hit on {primaryModel}, falling back to {fallbackModel}: {firstEx.Message}");

                try
                {
                    return await SendGenerateRequestAsync(prompt, fallbackModel, images);
                }
                catch (GeminiRateLimitException secondEx)
                {
                    throw new GeminiAllModelsRateLimitedException(primaryModel, fallbackModel, secondEx);
                }
            }
        }

        private static string NormalizeModelPath(string modelName) =>
            modelName.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? modelName
                : $"models/{modelName}";

        private static bool IsRateLimitError(HttpStatusCode statusCode, string responseBody)
        {
            // HTTP 429 is the standard rate-limit status code
            if (statusCode == HttpStatusCode.TooManyRequests)
                return true;

            // The Gemini API may also return 200 with an error body, or 400/403 with
            // quota-related messages. Check for known rate-limit indicators.
            if (string.IsNullOrEmpty(responseBody))
                return false;

            var bodyLower = responseBody.ToLowerInvariant();
            return bodyLower.Contains("rate limit")
                || bodyLower.Contains("resource_exhausted")
                || bodyLower.Contains("quota")
                || bodyLower.Contains("requests per minute")
                || bodyLower.Contains("tokens per minute")
                || bodyLower.Contains("requests per day");
        }

        private async Task<string> SendGenerateRequestAsync(string prompt, string modelName, IReadOnlyList<(string MimeType, string Base64Data)>? images = null)
        {
            var modelPath = NormalizeModelPath(modelName);

            var endpoint = $"{BaseUrl}/{modelPath}:generateContent";
            var url = endpoint;

            // Build JSON manually to avoid reflection-based serialization issues with trimming
            // Escape the prompt string properly for JSON
            var escapedPrompt = prompt
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");

            var jsonBuilder = new StringBuilder();
            jsonBuilder.AppendLine("{");
            jsonBuilder.AppendLine("  \"contents\": [");
            jsonBuilder.AppendLine("    {");
            jsonBuilder.AppendLine("      \"parts\": [");
            jsonBuilder.Append($"        {{\"text\": \"{escapedPrompt}\"}}");

            if (images is { Count: > 0 })
            {
                foreach (var (mimeType, base64Data) in images)
                {
                    jsonBuilder.AppendLine(",");
                    jsonBuilder.Append($"        {{\"inline_data\": {{\"mime_type\": \"{mimeType}\", \"data\": \"{base64Data}\"}}}}");
                }
            }

            jsonBuilder.AppendLine();
            jsonBuilder.AppendLine("      ]");
            jsonBuilder.AppendLine("    }");
            jsonBuilder.AppendLine("  ],");
            jsonBuilder.AppendLine("  \"generationConfig\": {");
            jsonBuilder.AppendLine("    \"temperature\": 0.3,");
            jsonBuilder.AppendLine("    \"maxOutputTokens\": 8192");
            jsonBuilder.AppendLine("  }");
            jsonBuilder.Append("}");

            var json = jsonBuilder.ToString();

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content);
            var responseStr = await response.Content.ReadAsStringAsync();

            if (IsRateLimitError(response.StatusCode, responseStr))
                throw new GeminiRateLimitException(modelPath, responseStr);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"API error ({(int)response.StatusCode}): {responseStr}");

            using var doc = JsonDocument.Parse(responseStr);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var errorMessage = error.GetProperty("message").GetString() ?? string.Empty;
                if (IsRateLimitError(HttpStatusCode.OK, errorMessage))
                    throw new GeminiRateLimitException(modelPath, errorMessage);

                throw new Exception(errorMessage);
            }

            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var contentEl)
                    && contentEl.TryGetProperty("parts", out var parts)
                    && parts.GetArrayLength() > 0)
                {
                    // Gemini 2.5+ may return a "thought" part (thinking tokens) before the
                    // actual response part. Skip any part marked with "thought": true and
                    // return the first real text part.
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("thought", out var isThought) && isThought.GetBoolean())
                            continue;
                        if (part.TryGetProperty("text", out var textEl))
                            return textEl.GetString() ?? "No response received.";
                    }
                }
            }

            return "No response received.";
        }
    }

    /// <summary>
    /// Thrown when the Gemini API returns a rate-limit or quota-exhaustion response
    /// (requests per minute, tokens per minute, or requests per day).
    /// </summary>
    public class GeminiRateLimitException : Exception
    {
        public string Model { get; }

        public GeminiRateLimitException(string model, string responseBody)
            : base($"Rate limit exceeded for {model}: {responseBody}")
        {
            Model = model;
        }
    }

    /// <summary>
    /// Thrown when both the primary and fallback Gemini models are rate-limited.
    /// </summary>
    public class GeminiAllModelsRateLimitedException : Exception
    {
        public string PrimaryModel { get; }
        public string FallbackModel { get; }

        public GeminiAllModelsRateLimitedException(string primaryModel, string fallbackModel, Exception inner)
            : base($"All Gemini models rate-limited ({primaryModel}, {fallbackModel}). Please try again later.", inner)
        {
            PrimaryModel = primaryModel;
            FallbackModel = fallbackModel;
        }
    }
}