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

        public SapHacService(string hacBaseUrl)
        {
            _hacBaseUrl = hacBaseUrl.TrimEnd('/');
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public void Dispose() => _client.Dispose();

        /// <summary>Authenticate with HAC credentials.</summary>
        public async Task<bool> LoginAsync(string username, string password)
        {
            try
            {
                // Get CSRF token from login page
                var loginPage = await _client.GetStringAsync($"{_hacBaseUrl}/j_spring_security_check");
                var csrf = ExtractCsrfToken(loginPage);

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
            var results = new List<CronJobEntry>();

            try
            {
                var html = await _client.GetStringAsync($"{_hacBaseUrl}/monitoring/cronjobs");

                bool firstRow = true;
                foreach (Match row in s_htmlRowRegex.Matches(html))
                {
                    if (firstRow) { firstRow = false; continue; }
                    var cells = s_htmlCellRegex.Matches(row.Value);
                    if (cells.Count < 4) continue;

                    string Clean(string s) => s_htmlTagRegex.Replace(s, "").Trim();
                    results.Add(new CronJobEntry(
                        Code: Clean(cells[0].Groups[1].Value),
                        Status: Clean(cells[1].Groups[1].Value),
                        LastResult: Clean(cells[2].Groups[1].Value),
                        NextActivationTime: cells.Count > 3 ? Clean(cells[3].Groups[1].Value) : "-",
                        TriggerActive: cells.Count > 4 ? Clean(cells[4].Groups[1].Value) : "-"
                    ));
                }
            }
            catch (Exception ex)
            {
                results.Add(new CronJobEntry("Error", ex.Message, "", "", ""));
            }

            return results;
        }

        /// <summary>Execute a FlexibleSearch query.</summary>
        public async Task<FlexibleSearchResult> RunFlexibleSearchAsync(string query, int maxResults = 100)
        {
            EnsureLoggedIn();

            try
            {
                var page = await _client.GetStringAsync($"{_hacBaseUrl}/console/flexsearch");
                var csrf = ExtractCsrfToken(page);

                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["flexibleSearchQuery"] = query,
                    ["maxCount"] = maxResults.ToString(),
                    ["_csrf"] = csrf ?? string.Empty
                });

                var response = await _client.PostAsync($"{_hacBaseUrl}/console/flexsearch/execute", form);
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
                var page = await _client.GetStringAsync($"{_hacBaseUrl}/console/impex/import");
                var csrf = ExtractCsrfToken(page);

                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["scriptContent"] = script,
                    ["encoding"] = "UTF-8",
                    ["enableCodeExecution"] = enableCodeExecution ? "true" : "false",
                    ["_csrf"] = csrf ?? string.Empty
                });

                var response = await _client.PostAsync($"{_hacBaseUrl}/console/impex/import/upload-script", form);
                var log = await response.Content.ReadAsStringAsync();
                bool success = log.Contains("Import finished successfully", StringComparison.OrdinalIgnoreCase)
                            || response.IsSuccessStatusCode;

                return new ImpExResult(success, log);
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

        // ── Helpers ──────────────────────────────────────────────────

        private void EnsureLoggedIn()
        {
            if (!_loggedIn)
                throw new InvalidOperationException("Not authenticated with HAC. Call LoginAsync first.");
        }

        private static string? ExtractCsrfToken(string html)
        {
            var match = Regex.Match(html, @"name=""_csrf""\s+value=""([^""]+)""", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
