using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    /// <summary>
    /// Lightweight local HTTP API so automation suites (Playwright, Cypress, etc.)
    /// can query test cases and submit execution results.
    /// </summary>
    public sealed class AutomationApiService : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private int _port;

        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public bool IsRunning => _listener?.IsListening == true;
        public int Port => _port;

        public void Start(int port = 5248)
        {
            if (IsRunning) return;

            _port = port;
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");

            try
            {
                _listener.Start();
                _listenTask = Task.Run(() => ListenLoop(_cts.Token));
                System.Diagnostics.Debug.WriteLine($"AutomationApiService started on port {_port}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AutomationApiService start failed: {ex.Message}");
                _listener = null;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            System.Diagnostics.Debug.WriteLine("AutomationApiService stopped");
        }

        public void Dispose() => Stop();

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener is { IsListening: true })
            {
                try
                {
                    var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                    _ = Task.Run(() => HandleRequest(ctx), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        // ── Request router ───────────────────────────────────────

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            try
            {
                var path = req.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // All routes start with /api
                if (segments.Length < 1 || segments[0] != "api")
                {
                    await WriteJson(res, 404, new { error = "Not found" });
                    return;
                }

                // GET /api/projects
                if (req.HttpMethod == "GET" && segments is ["api", "projects"])
                {
                    await HandleGetProjects(res);
                    return;
                }

                // Routes scoped to a project: /api/projects/{id}/...
                if (segments.Length >= 3 && segments[1] == "projects" && Guid.TryParse(segments[2], out var projectId))
                {
                    var projects = await LoadProjects();
                    var project = projects.FirstOrDefault(p => p.Id == projectId);

                    if (project == null)
                    {
                        await WriteJson(res, 404, new { error = "Project not found" });
                        return;
                    }

                    // GET /api/projects/{id}/testplans
                    if (req.HttpMethod == "GET" && segments is ["api", "projects", _, "testplans"])
                    {
                        await HandleGetTestPlans(res, project);
                        return;
                    }

                    // GET /api/projects/{id}/testcases
                    if (req.HttpMethod == "GET" && segments is ["api", "projects", _, "testcases"])
                    {
                        var planIdParam = req.QueryString["planId"];
                        Guid? planId = Guid.TryParse(planIdParam, out var pid) ? pid : null;
                        await HandleGetTestCases(res, project, planId);
                        return;
                    }

                    // GET /api/projects/{id}/testcases/{tcId}
                    if (req.HttpMethod == "GET" && segments.Length == 4 && segments[3].StartsWith("testcases:"))
                    {
                        // alternate: /api/projects/{id}/testcases/{tcGuid}
                    }
                    if (req.HttpMethod == "GET" && segments is ["api", "projects", _, "testcases", _]
                        && Guid.TryParse(segments[4], out var tcGuid))
                    {
                        await HandleGetTestCase(res, project, tcGuid);
                        return;
                    }

                    // GET /api/projects/{id}/executions
                    if (req.HttpMethod == "GET" && segments is ["api", "projects", _, "executions"])
                    {
                        await HandleGetExecutions(res, project);
                        return;
                    }

                    // POST /api/projects/{id}/executions
                    if (req.HttpMethod == "POST" && segments is ["api", "projects", _, "executions"])
                    {
                        await HandlePostExecution(req, res, project, projects);
                        return;
                    }

                    // POST /api/projects/{id}/executions/batch
                    if (req.HttpMethod == "POST" && segments is ["api", "projects", _, "executions", "batch"])
                    {
                        await HandlePostExecutionBatch(req, res, project, projects);
                        return;
                    }
                }

                await WriteJson(res, 404, new { error = "Not found" });
            }
            catch (Exception ex)
            {
                await WriteJson(res, 500, new { error = ex.Message });
            }
        }

        // ── Handlers ─────────────────────────────────────────────

        private async Task HandleGetProjects(HttpListenerResponse res)
        {
            var projects = await LoadProjects();
            var result = projects.Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                TestPlanCount = p.TestPlans.Count,
                TestCaseCount = p.TestCases.Count,
                TestExecutionCount = p.TestExecutions.Count
            });
            await WriteJson(res, 200, result);
        }

        private static Task HandleGetTestPlans(HttpListenerResponse res, Project project)
        {
            var result = project.TestPlans
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    p.Id,
                    p.TestPlanId,
                    p.Name,
                    p.Description,
                    p.CreatedAt,
                    Source = p.Source.ToString(),
                    TestCaseCount = project.TestCases.Count(tc => tc.TestPlanId == p.Id)
                });
            return WriteJson(res, 200, result);
        }

        private static Task HandleGetTestCases(HttpListenerResponse res, Project project, Guid? planId)
        {
            var cases = project.TestCases.AsEnumerable();
            if (planId.HasValue)
                cases = cases.Where(tc => tc.TestPlanId == planId.Value);

            var result = cases
                .OrderBy(tc => tc.TestCaseId)
                .Select(tc => MapTestCase(tc, project));
            return WriteJson(res, 200, result);
        }

        private static Task HandleGetTestCase(HttpListenerResponse res, Project project, Guid tcGuid)
        {
            var tc = project.TestCases.FirstOrDefault(c => c.Id == tcGuid);
            if (tc == null)
                return WriteJson(res, 404, new { error = "Test case not found" });

            return WriteJson(res, 200, MapTestCase(tc, project));
        }

        private static Task HandleGetExecutions(HttpListenerResponse res, Project project)
        {
            var result = project.TestExecutions
                .OrderByDescending(e => e.ExecutedAt)
                .Select(e =>
                {
                    var tc = project.TestCases.FirstOrDefault(c => c.Id == e.TestCaseId);
                    var plan = project.TestPlans.FirstOrDefault(p => p.Id == e.TestPlanId);
                    return new
                    {
                        e.Id,
                        e.ExecutionId,
                        e.TestCaseId,
                        TestCaseTitle = tc?.Title ?? "Deleted",
                        TestCaseDisplayId = tc?.TestCaseId ?? "N/A",
                        e.TestPlanId,
                        TestPlanDisplayId = plan?.TestPlanId ?? "N/A",
                        Result = e.Result.ToString(),
                        e.ActualResult,
                        e.Notes,
                        e.ExecutedAt
                    };
                });
            return WriteJson(res, 200, result);
        }

        private async Task HandlePostExecution(HttpListenerRequest req, HttpListenerResponse res,
            Project project, List<Project> allProjects)
        {
            var body = await ReadBody(req);
            var dto = JsonSerializer.Deserialize<ExecutionSubmission>(body, s_jsonOptions);

            if (dto == null)
            {
                await WriteJson(res, 400, new { error = "Invalid request body" });
                return;
            }

            // Resolve test case by GUID or display ID (e.g. "TC-001")
            TestCase? tc = null;
            if (dto.TestCaseId.HasValue)
                tc = project.TestCases.FirstOrDefault(c => c.Id == dto.TestCaseId.Value);

            if (tc == null && !string.IsNullOrEmpty(dto.TestCaseDisplayId))
                tc = project.TestCases.FirstOrDefault(c =>
                    string.Equals(c.TestCaseId, dto.TestCaseDisplayId, StringComparison.OrdinalIgnoreCase));

            if (tc == null)
            {
                await WriteJson(res, 404, new { error = "Test case not found. Provide testCaseId (GUID) or testCaseDisplayId (e.g. TC-001)." });
                return;
            }

            var status = ParseStatus(dto.Result);

            var execution = new TestExecution
            {
                ExecutionId = NextExecutionId(project),
                TestCaseId = tc.Id,
                TestPlanId = tc.TestPlanId ?? Guid.Empty,
                Result = status,
                ActualResult = Truncate(dto.ActualResult ?? string.Empty, 10_000),
                Notes = Truncate(dto.Notes ?? string.Empty, 10_000),
                ExecutedAt = DateTime.Now
            };
            project.TestExecutions.Add(execution);
            tc.Status = status;
            if (!string.IsNullOrEmpty(dto.ActualResult))
                tc.ActualResult = Truncate(dto.ActualResult, 10_000);

            await SaveProjects(allProjects);

            await WriteJson(res, 201, new
            {
                execution.Id,
                execution.ExecutionId,
                execution.TestCaseId,
                TestCaseDisplayId = tc.TestCaseId,
                Result = execution.Result.ToString(),
                execution.ExecutedAt
            });
        }

        private async Task HandlePostExecutionBatch(HttpListenerRequest req, HttpListenerResponse res,
            Project project, List<Project> allProjects)
        {
            var body = await ReadBody(req);
            var dtos = JsonSerializer.Deserialize<List<ExecutionSubmission>>(body, s_jsonOptions);

            if (dtos == null || dtos.Count == 0)
            {
                await WriteJson(res, 400, new { error = "Invalid or empty request body" });
                return;
            }

            var results = new List<object>();

            foreach (var dto in dtos)
            {
                TestCase? tc = null;
                if (dto.TestCaseId.HasValue)
                    tc = project.TestCases.FirstOrDefault(c => c.Id == dto.TestCaseId.Value);
                if (tc == null && !string.IsNullOrEmpty(dto.TestCaseDisplayId))
                    tc = project.TestCases.FirstOrDefault(c =>
                        string.Equals(c.TestCaseId, dto.TestCaseDisplayId, StringComparison.OrdinalIgnoreCase));

                if (tc == null)
                {
                    results.Add(new { testCaseDisplayId = dto.TestCaseDisplayId, error = "Test case not found" });
                    continue;
                }

                var status = ParseStatus(dto.Result);

                var execution = new TestExecution
                {
                    ExecutionId = NextExecutionId(project),
                    TestCaseId = tc.Id,
                    TestPlanId = tc.TestPlanId ?? Guid.Empty,
                    Result = status,
                    ActualResult = Truncate(dto.ActualResult ?? string.Empty, 10_000),
                    Notes = Truncate(dto.Notes ?? string.Empty, 10_000),
                    ExecutedAt = DateTime.Now
                };
                project.TestExecutions.Add(execution);
                tc.Status = status;
                if (!string.IsNullOrEmpty(dto.ActualResult))
                    tc.ActualResult = Truncate(dto.ActualResult, 10_000);

                results.Add(new
                {
                    execution.Id,
                    execution.ExecutionId,
                    execution.TestCaseId,
                    TestCaseDisplayId = tc.TestCaseId,
                    Result = execution.Result.ToString(),
                    execution.ExecutedAt
                });
            }

            await SaveProjects(allProjects);
            await WriteJson(res, 201, results);
        }

        // ── Helpers ──────────────────────────────────────────────

        private static object MapTestCase(TestCase tc, Project project)
        {
            var plan = project.TestPlans.FirstOrDefault(p => p.Id == tc.TestPlanId);
            return new
            {
                tc.Id,
                tc.TestCaseId,
                tc.Title,
                tc.PreConditions,
                tc.TestSteps,
                tc.TestData,
                tc.ExpectedResult,
                tc.ActualResult,
                Status = tc.Status.ToString(),
                tc.GeneratedAt,
                tc.SourceIssueId,
                Source = tc.Source.ToString(),
                tc.TestPlanId,
                TestPlanDisplayId = plan?.TestPlanId ?? "N/A",
                ExecutionCount = project.TestExecutions.Count(e => e.TestCaseId == tc.Id)
            };
        }

        private static TestCaseStatus ParseStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return TestCaseStatus.NotRun;
            if (Enum.TryParse<TestCaseStatus>(value, ignoreCase: true, out var s))
                return s;
            return TestCaseStatus.NotRun;
        }

        private static string NextExecutionId(Project project)
        {
            int max = project.TestExecutions
                .Select(te => int.TryParse(te.ExecutionId.Replace("TE-", ""), out var n) ? n : 0)
                .DefaultIfEmpty(0).Max();
            return $"TE-{max + 1:D3}";
        }

        private static string Truncate(string value, int maxLength)
            => value.Length > maxLength ? value[..maxLength] : value;

        private static async Task<List<Project>> LoadProjects()
            => await StorageService.Instance.LoadProjectsAsync();

        private static async Task SaveProjects(List<Project> projects)
            => await StorageService.Instance.SaveProjectsAsync(projects);

        private static async Task<string> ReadBody(HttpListenerRequest req)
        {
            using var reader = new System.IO.StreamReader(req.InputStream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        private static async Task WriteJson(HttpListenerResponse res, int statusCode, object data)
        {
            res.StatusCode = statusCode;
            res.ContentType = "application/json; charset=utf-8";
            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
            res.Close();
        }

        // ── DTO for execution submissions ────────────────────────

        private sealed class ExecutionSubmission
        {
            public Guid? TestCaseId { get; set; }
            public string? TestCaseDisplayId { get; set; }
            public string? Result { get; set; }
            public string? ActualResult { get; set; }
            public string? Notes { get; set; }
        }
    }
}
