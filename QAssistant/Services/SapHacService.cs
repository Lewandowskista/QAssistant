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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QAssistant.Services
{
    public record CronJobEntry(string Code, string Status, string LastResult, string NextActivationTime, string TriggerActive);
    public record CronJobHistoryEntry(string JobCode, string Status, string Result, string StartTime, string EndTime, string Duration);
    public record CatalogVersionInfo(string CatalogId, string Version, int ItemCount, string Status);
    public record CatalogSyncDiff(
        string CatalogId,
        int StagedCount,
        int OnlineCount,
        List<string> MissingInOnline,
        DateTime CheckedAt)
    {
        public int Delta => StagedCount - OnlineCount;
        public bool IsInSync => Delta == 0 && MissingInOnline.Count == 0;
        public string SyncStatus => IsInSync ? "In Sync" : Delta > 0 ? "Out of Sync" : "Online Ahead";
    }

    public record FlexibleSearchResult(List<string> Headers, List<List<string>> Rows, string Error);
    public record ImpExResult(bool Success, string Log);

    /// <summary>
    /// Connects to SAP Commerce HAC (Hybris Administration Console) to monitor
    /// CronJobs, query catalog versions, run FlexibleSearch, and import ImpEx.
    /// </summary>
    public sealed class SapHacService : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _hacBaseUrl;
        private bool _loggedIn;

        private static readonly Regex s_htmlRowRegex = new(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex s_htmlCellRegex = new(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex s_htmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

        public SapHacService(string hacBaseUrl, bool ignoreSslErrors = false)
        {
            _hacBaseUrl = hacBaseUrl.TrimEnd('/');
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
            };
            if (ignoreSslErrors)
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public void Dispose() => _client.Dispose();

        /// <summary>Authenticate with HAC credentials.</summary>
        public async Task<bool> LoginAsync(string username, string password)
        {
            try
            {
                // Fetch the CSRF token from the login form page.
                // Different HAC deployments serve the form at different paths, so try each in order.
                string? csrf = null;
                foreach (var path in new[] { "/login", "/hac/login", "/j_spring_security_check" })
                {
                    try
                    {
                        var page = await _client.GetStringAsync($"{_hacBaseUrl}{path}");
                        csrf = ExtractCsrfToken(page);
                        if (csrf != null) break;
                    }
                    catch { }
                }

                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["j_username"] = username,
                    ["j_password"] = password,
                    ["_csrf"] = csrf ?? string.Empty
                });

                var response = await _client.PostAsync($"{_hacBaseUrl}/j_spring_security_check", form);
                _loggedIn = response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Found;
                return _loggedIn;
            }
            catch
            {
                _loggedIn = false;
                return false;
            }
        }

        /// <summary>List all CronJob statuses from HAC monitoring.</summary>
        public async Task<List<CronJobEntry>> GetCronJobsAsync()
        {
            EnsureLoggedIn();

            // Try the JSON REST endpoint available in SAP Commerce 2005+ first;
            // fall back to HTML scraping for older HAC versions.
            return await TryGetCronJobsViaRestAsync() ?? await ParseCronJobsFromHtmlAsync();
        }

        /// <summary>Execute a FlexibleSearch query.</summary>
        public async Task<FlexibleSearchResult> RunFlexibleSearchAsync(string query, int maxResults = 100)
        {
            EnsureLoggedIn();

            try
            {
                var response = await PostWithCsrfRetryAsync(
                    "/console/flexsearch",
                    "/console/flexsearch/execute",
                    csrf => new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["flexibleSearchQuery"] = query,
                        ["maxCount"] = maxResults.ToString(),
                        ["_csrf"] = csrf ?? string.Empty
                    }));

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("exception", out var ex) && ex.GetString() is { Length: > 0 } errMsg)
                    return new FlexibleSearchResult([], [], errMsg);

                var headers = new List<string>();
                var rows = new List<List<string>>();

                if (root.TryGetProperty("headers", out var hdrs))
                    foreach (var h in hdrs.EnumerateArray())
                        headers.Add(h.GetString() ?? "");

                if (root.TryGetProperty("resultList", out var resultList))
                    foreach (var row in resultList.EnumerateArray())
                    {
                        var cells = new List<string>();
                        foreach (var cell in row.EnumerateArray())
                            cells.Add(cell.ValueKind == JsonValueKind.Null ? "" : cell.ToString());
                        rows.Add(cells);
                    }

                return new FlexibleSearchResult(headers, rows, string.Empty);
            }
            catch (Exception ex)
            {
                return new FlexibleSearchResult([], [], ex.Message);
            }
        }

        /// <summary>Import an ImpEx script via HAC.</summary>
        public async Task<ImpExResult> ImportImpExAsync(string script, bool enableCodeExecution = false)
        {
            EnsureLoggedIn();

            try
            {
                var response = await PostWithCsrfRetryAsync(
                    "/console/impex/import",
                    "/console/impex/import/upload-script",
                    csrf => new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["scriptContent"] = script,
                        ["encoding"] = "UTF-8",
                        ["enableCodeExecution"] = enableCodeExecution ? "true" : "false",
                        ["_csrf"] = csrf ?? string.Empty
                    }));

                var body = await response.Content.ReadAsStringAsync();

                // Modern HAC (2005+) returns JSON; older versions return HTML
                if (body.TrimStart().StartsWith('{'))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        var root = doc.RootElement;
                        bool hasError = root.TryGetProperty("hasError", out var errFlag) && errFlag.GetBoolean();
                        string log = GetJsonString(root, "initMessage", "exceptionMessage", "log") ?? body;
                        return new ImpExResult(!hasError, log);
                    }
                    catch { }
                }

                // HTML / plain-text fallback
                bool success = body.Contains("Import finished successfully", StringComparison.OrdinalIgnoreCase)
                            || response.IsSuccessStatusCode;
                return new ImpExResult(success, body);
            }
            catch (Exception ex)
            {
                return new ImpExResult(false, ex.Message);
            }
        }

        /// <summary>
        /// Job code substrings that are considered critical. A job is critical when its
        /// code contains any entry here (case-insensitive). Used by alert detection.
        /// </summary>
        public static readonly string[] CriticalJobCodes =
        [
            "solrindexer", "catalogsync", "updateindex", "processTask",
            "fullindex", "cleanUpCronJob", "triggerReIndex", "syncCronJob"
        ];

        /// <summary>Returns CronJob entries whose code matches a known critical job pattern.</summary>
        public async Task<List<CronJobEntry>> GetCriticalJobStatusAsync()
        {
            var all = await GetCronJobsAsync();
            return all
                .Where(j => CriticalJobCodes.Any(c => j.Code.Contains(c, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        /// <summary>
        /// Retrieves CronJobHistory entries via FlexibleSearch.
        /// Pass <paramref name="jobCode"/> to scope to a single job, or null for the most recent across all jobs.
        /// </summary>
        public async Task<List<CronJobHistoryEntry>> GetCronJobHistoryAsync(string? jobCode = null, int maxEntries = 50)
        {
            EnsureLoggedIn();
            var safeCode = jobCode?.Replace("'", "''");
            var whereClause = string.IsNullOrWhiteSpace(safeCode)
                ? ""
                : $" WHERE {{cj.code}}='{safeCode}'";

            var query = $"SELECT {{cj.code}},{{cjh.startTime}},{{cjh.endTime}},{{cjh.status}},{{cjh.result}} FROM {{CronJobHistory AS cjh JOIN CronJob AS cj ON {{cjh.cronjob}}={{cj.pk}}}}{whereClause} ORDER BY {{cjh.startTime}} DESC";

            var result = await RunFlexibleSearchAsync(query, maxEntries);
            var list = new List<CronJobHistoryEntry>();
            if (!string.IsNullOrEmpty(result.Error))
                return list;

            foreach (var row in result.Rows)
            {
                if (row.Count < 5) continue;
                list.Add(new CronJobHistoryEntry(
                    JobCode:   row[0],
                    Status:    row[3],
                    Result:    row[4],
                    StartTime: row[1],
                    EndTime:   row[2],
                    Duration:  ComputeDuration(row[1], row[2])
                ));
            }
            return list;
        }

        private static string ComputeDuration(string startTime, string endTime)
        {
            if (DateTime.TryParse(startTime, out var s) && DateTime.TryParse(endTime, out var e) && e > s)
            {
                var d = e - s;
                return d.TotalHours >= 1
                    ? $"{(int)d.TotalHours}h {d.Minutes}m"
                    : d.TotalMinutes >= 1
                    ? $"{(int)d.TotalMinutes}m {d.Seconds}s"
                    : $"{d.Seconds}s";
            }
            return "-";
        }

        /// <summary>Get catalog version item counts via FlexibleSearch.</summary>
        public async Task<List<CatalogVersionInfo>> GetCatalogVersionsAsync()
        {
            const string query = "SELECT {cv.catalog},{cv.version},COUNT({p.pk}) FROM {Product AS p JOIN CatalogVersion AS cv ON {p.catalogVersion}={cv.pk}} GROUP BY {cv.catalog},{cv.version}";
            var result = await RunFlexibleSearchAsync(query, 200);

            var list = new List<CatalogVersionInfo>();
            foreach (var row in result.Rows)
            {
                if (row.Count >= 3)
                    list.Add(new CatalogVersionInfo(row[0], row[1], int.TryParse(row[2], out var n) ? n : 0, "OK"));
            }
            return list;
        }

        /// <summary>Returns all catalog IDs defined in this Commerce instance.</summary>
        public async Task<List<string>> GetCatalogIdsAsync()
        {
            const string query = "SELECT {c.id} FROM {Catalog AS c} ORDER BY {c.id}";
            var result = await RunFlexibleSearchAsync(query, 50);
            return result.Rows
                .Where(r => r.Count > 0 && !string.IsNullOrEmpty(r[0]))
                .Select(r => r[0])
                .ToList();
        }

        /// <summary>
        /// Compare Staged vs Online item counts for a given catalog and collect product codes
        /// present in Staged but absent from Online (capped at <paramref name="maxMissing"/>).
        /// </summary>
        public async Task<CatalogSyncDiff> GetCatalogSyncDiffAsync(string catalogId, int maxMissing = 200)
        {
            EnsureLoggedIn();
            var safeId = catalogId.Replace("'", "''");

            var stagedQ = "SELECT {p.code} FROM {Product AS p JOIN CatalogVersion AS cv ON {p.catalogVersion}={cv.pk}} WHERE {cv.catalog(id)}='" + safeId + "' AND {cv.version}='Staged'";
            var onlineQ = "SELECT {p.code} FROM {Product AS p JOIN CatalogVersion AS cv ON {p.catalogVersion}={cv.pk}} WHERE {cv.catalog(id)}='" + safeId + "' AND {cv.version}='Online'";

            var stagedResult = await RunFlexibleSearchAsync(stagedQ, 5000);
            var onlineResult = await RunFlexibleSearchAsync(onlineQ, 5000);

            var stagedCodes = stagedResult.Rows
                .Where(r => r.Count > 0 && !string.IsNullOrEmpty(r[0]))
                .Select(r => r[0])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var onlineCodes = onlineResult.Rows
                .Where(r => r.Count > 0 && !string.IsNullOrEmpty(r[0]))
                .Select(r => r[0])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = stagedCodes.Except(onlineCodes).Take(maxMissing).ToList();

            return new CatalogSyncDiff(catalogId, stagedCodes.Count, onlineCodes.Count, missing, DateTime.Now);
        }

        // ── REST / HTML parsing helpers ───────────────────────────────

        /// <summary>
        /// Attempts to retrieve CronJob data from the JSON REST endpoint available
        /// in SAP Commerce 2005+. Returns null when the endpoint is unavailable or
        /// returns non-JSON content, signalling an older HAC version.
        /// </summary>
        private async Task<List<CronJobEntry>?> TryGetCronJobsViaRestAsync()
        {
            try
            {
                var response = await _client.GetAsync($"{_hacBaseUrl}/monitoring/cronjobs/data");
                if (!response.IsSuccessStatusCode) return null;

                var content = await response.Content.ReadAsStringAsync();
                var trimmed = content.TrimStart();
                if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[')) return null;

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Locate the data array — property name varies across Commerce versions
                JsonElement dataArray = default;
                bool found = root.TryGetProperty("cronJobData", out dataArray)
                          || root.TryGetProperty("cronJobTableData", out dataArray)
                          || root.TryGetProperty("data", out dataArray);

                if (!found)
                {
                    if (root.ValueKind == JsonValueKind.Array)
                        dataArray = root;
                    else
                        return null;
                }

                var results = new List<CronJobEntry>();
                foreach (var item in dataArray.EnumerateArray())
                {
                    var code = GetJsonString(item, "jobCode", "code") ?? string.Empty;
                    if (string.IsNullOrEmpty(code)) continue;

                    bool? active = null;
                    if (item.TryGetProperty("triggerActive", out var ta) || item.TryGetProperty("active", out ta))
                        active = ta.ValueKind is JsonValueKind.True or JsonValueKind.False ? ta.GetBoolean() : null;

                    results.Add(new CronJobEntry(
                        Code: code,
                        Status: GetJsonString(item, "jobStatus", "status") ?? string.Empty,
                        LastResult: GetJsonString(item, "jobResult", "result") ?? string.Empty,
                        NextActivationTime: GetJsonString(item, "nextActivationTime", "nextActivation") ?? "-",
                        TriggerActive: active.HasValue ? active.Value.ToString() : "-"
                    ));
                }

                return results.Count > 0 ? results : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parses CronJob entries from the HAC monitoring HTML page.
        /// Narrows the search to the first data table to avoid false matches
        /// on page navigation or other surrounding chrome.
        /// </summary>
        private async Task<List<CronJobEntry>> ParseCronJobsFromHtmlAsync()
        {
            var results = new List<CronJobEntry>();
            try
            {
                var html = await _client.GetStringAsync($"{_hacBaseUrl}/monitoring/cronjobs");

                // Narrow to the first <table> to avoid matching page chrome rows
                var tableMatch = Regex.Match(html,
                    @"<table\b[^>]*>(.*?)</table>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var searchHtml = tableMatch.Success ? tableMatch.Value : html;

                bool firstRow = true;
                foreach (Match row in s_htmlRowRegex.Matches(searchHtml))
                {
                    if (firstRow) { firstRow = false; continue; }
                    var cells = s_htmlCellRegex.Matches(row.Value);
                    if (cells.Count < 4) continue;

                    string Clean(string s) => s_htmlTagRegex.Replace(s, "").Trim();
                    var code = Clean(cells[0].Groups[1].Value);
                    if (string.IsNullOrEmpty(code)) continue;

                    results.Add(new CronJobEntry(
                        Code: code,
                        Status: Clean(cells[1].Groups[1].Value),
                        LastResult: Clean(cells[2].Groups[1].Value),
                        NextActivationTime: cells.Count > 3 ? Clean(cells[3].Groups[1].Value) : "-",
                        TriggerActive: cells.Count > 4 ? Clean(cells[4].Groups[1].Value) : "-"
                    ));
                }
            }
            catch (Exception ex)
            {
                results.Add(new CronJobEntry("Error", ex.Message, string.Empty, string.Empty, string.Empty));
            }
            return results;
        }

        /// <summary>
        /// Performs a POST with CSRF protection, automatically retrying once when a
        /// 403 Forbidden response is received — which indicates the CSRF token expired
        /// during a long-running session.
        /// </summary>
        private async Task<HttpResponseMessage> PostWithCsrfRetryAsync(
            string csrfPagePath,
            string postPath,
            Func<string?, FormUrlEncodedContent> buildForm)
        {
            var page = await _client.GetStringAsync($"{_hacBaseUrl}{csrfPagePath}");
            var csrf = ExtractCsrfToken(page);
            var response = await _client.PostAsync($"{_hacBaseUrl}{postPath}", buildForm(csrf));

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // Token expired — re-fetch and retry once
                page = await _client.GetStringAsync($"{_hacBaseUrl}{csrfPagePath}");
                csrf = ExtractCsrfToken(page);
                response = await _client.PostAsync($"{_hacBaseUrl}{postPath}", buildForm(csrf));
            }

            return response;
        }

        /// <summary>Returns the string value of the first matching property name found in a JSON element.</summary>
        private static string? GetJsonString(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
                if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
            return null;
        }

        // ── Helpers ──────────────────────────────────────────────────

        private void EnsureLoggedIn()
        {
            if (!_loggedIn)
                throw new InvalidOperationException("Not authenticated with HAC. Call LoginAsync first.");
        }

        private static string? ExtractCsrfToken(string html)
        {
            // Pattern 1: <input name="_csrf" value="TOKEN"> (standard attribute order)
            var m = Regex.Match(html, @"name=""_csrf""\s+value=""([^""]+)""", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;

            // Pattern 2: <input value="TOKEN" name="_csrf"> (reversed attribute order)
            m = Regex.Match(html, @"value=""([^""]+)""\s+name=""_csrf""", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;

            // Pattern 3: <meta name="_csrf" content="TOKEN"> (used for XHR in some HAC versions)
            m = Regex.Match(html, @"<meta\s+name=""_csrf""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;

            // Pattern 4: JSON object embedded in a script block
            m = Regex.Match(html, @"""_csrf""\s*:\s*""([^""]+)""");
            if (m.Success) return m.Groups[1].Value;

            return null;
        }
    }
}
