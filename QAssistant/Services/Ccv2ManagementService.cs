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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace QAssistant.Services
{
    public record Ccv2Environment(
        string Code,
        string Name,
        string Status,
        string DeploymentStatus);

    public record Ccv2Deployment(
        string Code,
        string EnvironmentCode,
        string BuildCode,
        string Status,
        string Strategy,
        string CreatedAt,
        string DeployedAt);

    public record Ccv2Build(
        string Code,
        string Name,
        string BuildStatus,
        string AppVersion,
        string CreatedAt);

    /// <summary>
    /// Calls the SAP Commerce Cloud v2 (CCv2) Management API to fetch environment
    /// states, deployment history, and build metadata.
    /// Base URL: https://portalrotapi.hana.ondemand.com/v2/subscriptions/{subscriptionCode}/
    /// Auth: Bearer token generated in the SAP Cloud Portal under API Token Management.
    /// </summary>
    public sealed class Ccv2ManagementService : IDisposable
    {
        private const string DefaultApiBase = "https://portalrotapi.hana.ondemand.com";

        private readonly HttpClient _client;
        private readonly string _subscriptionCode;
        private readonly string _apiBase;

        public Ccv2ManagementService(string subscriptionCode, string apiToken, string? apiBaseUrl = null)
        {
            _subscriptionCode = subscriptionCode.Trim();
            _apiBase = apiBaseUrl?.TrimEnd('/') is { Length: > 0 } custom ? custom : DefaultApiBase;
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        }

        public void Dispose() => _client.Dispose();

        private string Url(string path) =>
            $"{_apiBase}/v2/subscriptions/{Uri.EscapeDataString(_subscriptionCode)}/{path.TrimStart('/')}";

        /// <summary>Fetch all environments for the subscription.</summary>
        public async Task<List<Ccv2Environment>> GetEnvironmentsAsync()
        {
            var json = await _client.GetStringAsync(Url("environments"));
            return ParseValueArray(json, item => new Ccv2Environment(
                Code: Str(item, "code"),
                Name: Str(item, "name"),
                Status: Str(item, "status"),
                DeploymentStatus: Str(item, "deploymentStatus")));
        }

        /// <summary>
        /// Fetch recent deployments, optionally scoped to one environment.
        /// Results are ordered newest first by scheduled timestamp.
        /// </summary>
        public async Task<List<Ccv2Deployment>> GetDeploymentsAsync(string? environmentCode = null, int top = 20)
        {
            var qs = $"$top={top}&$orderby=scheduledTimestamp%20desc";
            if (!string.IsNullOrEmpty(environmentCode))
                qs += $"&environmentCode={Uri.EscapeDataString(environmentCode)}";

            var json = await _client.GetStringAsync(Url($"deployments?{qs}"));
            return ParseValueArray(json, item => new Ccv2Deployment(
                Code: Str(item, "code"),
                EnvironmentCode: Str(item, "environmentCode"),
                BuildCode: Str(item, "buildCode"),
                Status: Str(item, "status"),
                Strategy: Str(item, "strategy"),
                CreatedAt: Str(item, "createdTimestamp"),
                DeployedAt: Str(item, "deployedTimestamp")));
        }

        /// <summary>Fetch a single build record by its code.</summary>
        public async Task<Ccv2Build?> GetBuildAsync(string buildCode)
        {
            try
            {
                var json = await _client.GetStringAsync(Url($"builds/{Uri.EscapeDataString(buildCode)}"));
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new Ccv2Build(
                    Code: Str(root, "code"),
                    Name: Str(root, "name"),
                    BuildStatus: Str(root, "buildStatus"),
                    AppVersion: Str(root, "applicationDefinitionVersion"),
                    CreatedAt: Str(root, "createdTimestamp"));
            }
            catch { return null; }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static List<T> ParseValueArray<T>(string json, Func<JsonElement, T> map)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var results = new List<T>();
            if (root.TryGetProperty("value", out var arr))
                foreach (var item in arr.EnumerateArray())
                    results.Add(map(item));
            return results;
        }

        private static string Str(JsonElement e, string key)
        {
            if (e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null)
                return v.GetString() ?? string.Empty;
            return string.Empty;
        }
    }
}
