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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QAssistant.Services
{
    public record CronJobEntry(string Code, string Status, string LastResult, string NextActivationTime, string TriggerActive);
    public record CatalogVersionInfo(string CatalogId, string Version, int ItemCount, string Status);
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

                // Parse table rows from the HTML response
                var rowPattern = new Regex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var cellPattern = new Regex(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var tagPattern = new Regex(@"<[^>]+>");

                bool firstRow = true;
                foreach (Match row in rowPattern.Matches(html))
                {
                    if (firstRow) { firstRow = false; continue; }
                    var cells = cellPattern.Matches(row.Value);
                    if (cells.Count < 4) continue;

                    string Clean(string s) => tagPattern.Replace(s, "").Trim();
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
