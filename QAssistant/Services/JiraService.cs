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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    public class JiraService : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly string _browseUrl;

        // Only allow valid Atlassian subdomain characters (alphanumeric + hyphens).
        private static readonly Regex s_safeDomainRegex = new(@"^[a-zA-Z0-9][a-zA-Z0-9\-]*$", RegexOptions.Compiled);

        public JiraService(string domain, string email, string apiToken)
        {
            if (!s_safeDomainRegex.IsMatch(domain))
                throw new ArgumentException("Invalid Jira domain. Use only your Atlassian subdomain (e.g. 'mycompany').", nameof(domain));

            _client = CreateClient(email, apiToken);
            // Ensure base URL targets the v3 Cloud REST API and includes trailing slash for concatenation
            _baseUrl = $"https://{domain}.atlassian.net/rest/api/3/";
            _browseUrl = $"https://{domain}.atlassian.net/browse";
        }

        // Helper to GET JSON with special handling for 410 Gone and other HTTP errors
        private async Task<JsonElement> GetJsonAsync(string url)
        {
            using var resp = await _client.GetAsync(url);
            if (resp.StatusCode == HttpStatusCode.Gone)
            {
                var body = await resp.Content.ReadAsStringAsync();
                Debug.WriteLine($"Jira API returned 410 Gone for '{url}'. Response body: {body}");

                // Attempt fallback to REST API v2
                var altUrl = url.Replace("/rest/api/3/", "/rest/api/2/");
                if (altUrl != url)
                {
                    Debug.WriteLine($"Attempting fallback to '{altUrl}'");
                    using var resp2 = await _client.GetAsync(altUrl);
                    if (resp2.StatusCode == HttpStatusCode.Gone)
                    {
                        var body2 = await resp2.Content.ReadAsStringAsync();
                        var msg = $"Jira API returned 410 Gone for '{url}' and fallback '{altUrl}'. Bodies: {body} | {body2}";
                        Debug.WriteLine(msg);
                        throw new HttpRequestException(msg, null, resp2.StatusCode);
                    }

                    resp2.EnsureSuccessStatusCode();
                    var stream2 = await resp2.Content.ReadAsStreamAsync();
                    using var doc2 = await JsonDocument.ParseAsync(stream2);
                    return doc2.RootElement.Clone();
                }

                var msgNoFallback = $"Jira API returned 410 Gone for '{url}'. The endpoint may be deprecated or the resource removed.";
                Debug.WriteLine(msgNoFallback);
                throw new HttpRequestException(msgNoFallback, null, resp.StatusCode);
            }

            resp.EnsureSuccessStatusCode();
            var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            return doc.RootElement.Clone();
        }

        // Helper to POST JSON and return response JSON with 410/400 handling
        private async Task<JsonElement> PostJsonAsync(string url, object payload)
        {
            using var resp = await _client.PostAsJsonAsync(url, payload);
            if (resp.StatusCode == HttpStatusCode.Gone)
            {
                var body = await resp.Content.ReadAsStringAsync();
                Debug.WriteLine($"Jira API returned 410 Gone for '{url}'. Response body: {body}");

                // Attempt fallback to REST API v2
                var altUrl = url.Replace("/rest/api/3/", "/rest/api/2/");
                if (altUrl != url)
                {
                    Debug.WriteLine($"Attempting POST fallback to '{altUrl}'");
                    using var resp2 = await _client.PostAsJsonAsync(altUrl, payload);
                    if (resp2.StatusCode == HttpStatusCode.Gone)
                    {
                        var body2 = await resp2.Content.ReadAsStringAsync();
                        var msg = $"Jira API returned 410 Gone for '{url}' and fallback '{altUrl}'. Bodies: {body} | {body2}";
                        Debug.WriteLine(msg);
                        throw new HttpRequestException(msg, null, resp2.StatusCode);
                    }

                    if (resp2.StatusCode == HttpStatusCode.BadRequest)
                    {
                        var body2 = await resp2.Content.ReadAsStringAsync();
                        var msg = $"Jira API returned 400 Bad Request for fallback '{altUrl}': {body2}";
                        Debug.WriteLine(msg);
                        throw new HttpRequestException(msg, null, resp2.StatusCode);
                    }

                    resp2.EnsureSuccessStatusCode();
                    var stream2 = await resp2.Content.ReadAsStreamAsync();
                    using var doc2 = await JsonDocument.ParseAsync(stream2);
                    return doc2.RootElement.Clone();
                }

                var msgNoFallback = $"Jira API returned 410 Gone for '{url}'. The endpoint may be deprecated or the resource removed.";
                Debug.WriteLine(msgNoFallback);
                throw new HttpRequestException(msgNoFallback, null, resp.StatusCode);
            }

            if (resp.StatusCode == HttpStatusCode.BadRequest)
            {
                var body = await resp.Content.ReadAsStringAsync();
                var msg = $"Jira API returned 400 Bad Request for '{url}': {body}";
                Debug.WriteLine(msg);
                throw new HttpRequestException(msg, null, resp.StatusCode);
            }

            resp.EnsureSuccessStatusCode();
            var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            return doc.RootElement.Clone();
        }

        public void Dispose() => _client.Dispose();

        private static HttpClient CreateClient(string email, string apiToken)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        public async Task<List<JiraProject>> GetProjectsAsync()
        {
            var root = await GetJsonAsync($"{_baseUrl}project");
            List<JsonElement>? projects = null;
            try
            {
                projects = root.Deserialize<List<JsonElement>>();
            }
            catch { projects = null; }

            return projects?.Select(node => new JiraProject
            {
                Id = node.GetProperty("id").GetString() ?? "",
                Key = node.GetProperty("key").GetString() ?? "",
                Name = node.GetProperty("name").GetString() ?? ""
            }).ToList() ?? new List<JiraProject>();
        }

        public async Task<List<ProjectTask>> GetIssuesAsync(string projectKey)
        {
            var jql = $"project={projectKey} ORDER BY updated DESC";
            // Use the updated JQL search endpoint: '/rest/api/3/search/jql'
            var url = $"{_baseUrl}search/jql";

            // Use an explicit string array for fields to avoid serialization ambiguity
            var fieldsArray = new[] { "summary", "description", "status", "priority", "assignee", "reporter", "issuetype", "duedate", "labels", "components" };

            var payload = new
            {
                jql = jql,
                startAt = 0,
                maxResults = 100,
                fields = fieldsArray
            };

            JsonElement response;
            try
            {
                response = await PostJsonAsync(url, payload);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                Debug.WriteLine($"POST to '{url}' returned 400, attempting GET fallback with querystring");
                // Fallback to GET with query string (some instances reject POST payloads)
                var qs = $"{url}?jql={Uri.EscapeDataString(jql)}&startAt=0&maxResults=100&fields={Uri.EscapeDataString(string.Join(",", fieldsArray))}";
                response = await GetJsonAsync(qs);
            }
            var tasks = new List<ProjectTask>();

            if (response.TryGetProperty("issues", out var issues))
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    var fields = issue.GetProperty("fields");
                    var task = new ProjectTask
                    {
                        Id = Guid.NewGuid(),
                        ExternalId = issue.GetProperty("id").GetString() ?? "",
                        IssueIdentifier = issue.GetProperty("key").GetString() ?? "",
                        Title = fields.GetProperty("summary").GetString() ?? "",
                        Description = GetJiraDescription(fields),
                        RawDescription = GetJiraDescription(fields),
                        Status = MapJiraStatus(fields.GetProperty("status").GetProperty("name").GetString() ?? ""),
                        Priority = MapJiraPriority(fields),
                        TicketUrl = $"{_browseUrl}/{issue.GetProperty("key").GetString()}",
                        Source = TaskSource.Jira,
                        IssueType = GetString(fields, "issuetype", "name"),
                        Assignee = GetString(fields, "assignee", "displayName"),
                        Reporter = GetString(fields, "reporter", "displayName"),
                        Labels = GetLabels(fields),
                        DueDate = GetDate(fields, "duedate")
                    };
                    tasks.Add(task);
                }
            }

            return tasks;
        }

        public async Task UpdateIssueStatusAsync(string issueIdOrKey, string transitionId)
        {
            var url = $"{_baseUrl}issue/{issueIdOrKey}/transitions";
            var payload = new { transition = new { id = transitionId } };
            try
            {
                await PostJsonAsync(url, payload);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Gone)
            {
                Debug.WriteLine($"Cannot transition issue {issueIdOrKey}: {ex.Message}");
                throw;
            }
        }

        public async Task AddCommentAsync(string issueIdOrKey, string body)
        {
            var url = $"{_baseUrl}issue/{issueIdOrKey}/comment";
            var payload = new
            {
                body = new
                {
                    type = "doc",
                    version = 1,
                    content = new[]
                    {
                        new
                        {
                            type = "paragraph",
                            content = new[] { new { type = "text", text = body } }
                        }
                    }
                }
            };
            try
            {
                await PostJsonAsync(url, payload);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Gone)
            {
                Debug.WriteLine($"Cannot add comment to {issueIdOrKey}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Creates a new Jira bug issue and returns its browse URL, or null on failure.
        /// </summary>
        public async Task<string?> CreateIssueAsync(string projectKey, string summary, string description, string priority = "Medium")
        {
            var url = $"{_baseUrl}issue";
            var descriptionAdf = new
            {
                type = "doc",
                version = 1,
                content = description.Split('\n')
                    .Select(line => (object)new
                    {
                        type = "paragraph",
                        content = new[] { new { type = "text", text = line } }
                    }).ToArray()
            };

            var payload = new
            {
                fields = new
                {
                    project = new { key = projectKey },
                    summary,
                    description = descriptionAdf,
                    issuetype = new { name = "Bug" },
                    priority = new { name = priority }
                }
            };

            try
            {
                var response = await PostJsonAsync(url, payload);
                if (response.TryGetProperty("key", out var keyEl))
                    return $"{_browseUrl}/{keyEl.GetString()}";
                return null;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Cannot create Jira issue: {ex.Message}");
                throw;
            }
        }

        public async Task<List<LinearComment>> GetCommentsAsync(string issueIdOrKey)
        {
            var url = $"{_baseUrl}issue/{issueIdOrKey}/comment?orderBy=-created";
            var response = await GetJsonAsync(url);
            var comments = new List<LinearComment>();

            if (response.TryGetProperty("comments", out var commentsArray))
            {
                foreach (var comment in commentsArray.EnumerateArray())
                {
                    string author = "";
                    if (comment.TryGetProperty("author", out var authorEl) &&
                        authorEl.ValueKind != JsonValueKind.Null)
                        author = authorEl.GetProperty("displayName").GetString() ?? "";

                    DateTime createdAt = DateTime.Now;
                    if (comment.TryGetProperty("created", out var createdEl))
                        DateTime.TryParse(createdEl.GetString(), out createdAt);

                    string body = "";
                    if (comment.TryGetProperty("body", out var bodyEl) &&
                        bodyEl.ValueKind != JsonValueKind.Null)
                        body = ExtractAdfText(bodyEl);

                    comments.Add(new LinearComment
                    {
                        Body = body,
                        AuthorName = author,
                        CreatedAt = createdAt
                    });
                }
            }

            return comments;
        }

        public async Task<List<JiraTransition>> GetTransitionsAsync(string issueIdOrKey)
        {
            var url = $"{_baseUrl}issue/{issueIdOrKey}/transitions";
            var response = await GetJsonAsync(url);
            var transitions = new List<JiraTransition>();

            if (response.TryGetProperty("transitions", out var transitionsArray))
            {
                foreach (var t in transitionsArray.EnumerateArray())
                {
                    transitions.Add(new JiraTransition
                    {
                        Id = t.GetProperty("id").GetString() ?? "",
                        Name = t.TryGetProperty("to", out var to)
                            ? to.GetProperty("name").GetString() ?? t.GetProperty("name").GetString() ?? ""
                            : t.GetProperty("name").GetString() ?? ""
                    });
                }
            }

            return transitions;
        }

        public async Task<List<WorklogEntry>> GetChangelogAsync(string issueIdOrKey)
        {
            var url = $"{_baseUrl}issue/{issueIdOrKey}?expand=changelog&fields=summary";
            var response = await GetJsonAsync(url);
            var entries = new List<WorklogEntry>();

            if (response.TryGetProperty("changelog", out var changelog) &&
                changelog.TryGetProperty("histories", out var histories))
            {
                foreach (var history in histories.EnumerateArray())
                {
                    string author = "";
                    if (history.TryGetProperty("author", out var authorEl) &&
                        authorEl.ValueKind != JsonValueKind.Null)
                        author = authorEl.GetProperty("displayName").GetString() ?? "";

                    DateTime created = DateTime.Now;
                    if (history.TryGetProperty("created", out var createdEl))
                        DateTime.TryParse(createdEl.GetString(), out created);

                    if (history.TryGetProperty("items", out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            entries.Add(new WorklogEntry
                            {
                                Timestamp = created,
                                Author = author,
                                Field = item.GetProperty("field").GetString() ?? "",
                                FromValue = item.TryGetProperty("fromString", out var fromEl)
                                    ? fromEl.GetString() ?? "" : "",
                                ToValue = item.TryGetProperty("toString", out var toEl)
                                    ? toEl.GetString() ?? "" : ""
                            });
                        }
                    }
                }
            }

            return entries;
        }

        public static string? FindMatchingTransitionId(Models.TaskStatus targetStatus, List<JiraTransition> transitions)
        {
            var candidateNames = targetStatus switch
            {
                Models.TaskStatus.Backlog => new[] { "Backlog", "To Do", "Open", "Reopen" },
                Models.TaskStatus.Todo => new[] { "To Do", "Open", "Backlog", "Reopen" },
                Models.TaskStatus.InProgress => new[] { "In Progress", "Start Progress", "In Development" },
                Models.TaskStatus.InReview => new[] { "In Review", "Code Review", "Testing", "QA" },
                Models.TaskStatus.Done => new[] { "Done", "Closed", "Resolved", "Close", "Resolve" },
                Models.TaskStatus.Canceled => new[] { "Canceled", "Cancelled", "Won't Do", "Rejected" },
                Models.TaskStatus.Duplicate => new[] { "Duplicate", "Won't Do" },
                _ => Array.Empty<string>()
            };

            foreach (var candidate in candidateNames)
            {
                var match = transitions.FirstOrDefault(t =>
                    t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Id;
            }

            return null;
        }

        // Recursive extractor for Atlassian Document Format (ADF) that preserves block boundaries
        private static string ExtractAdfText(JsonElement adfNode)
        {
            var sb = new StringBuilder();

            bool IsBlockType(JsonElement node)
            {
                if (node.ValueKind != JsonValueKind.Object) return false;
                if (!node.TryGetProperty("type", out var t)) return false;
                var s = t.GetString() ?? string.Empty;
                return s is "paragraph" or "heading" or "bulletList" or "orderedList" or "listItem" or "blockquote" or "table" or "codeBlock" or "panel";
            }

            void Recurse(JsonElement node)
            {
                if (node.ValueKind == JsonValueKind.Object)
                {
                    // If this node contains a direct text property, append it
                    if (node.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                        sb.Append(textEl.GetString());

                    // Recurse into content array if present
                    if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var child in content.EnumerateArray())
                        {
                            Recurse(child);
                        }
                    }

                    // Preserve block separation
                    if (IsBlockType(node)) sb.AppendLine();
                }
                else if (node.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in node.EnumerateArray()) Recurse(child);
                }
            }

            Recurse(adfNode);
            return sb.ToString().Trim();
        }

        private static string GetJiraDescription(JsonElement fields)
        {
            try
            {
                var desc = fields.GetProperty("description");
                if (desc.ValueKind == JsonValueKind.Null) return string.Empty;
                return ExtractAdfText(desc);
            }
            catch { return string.Empty; }
        }

        private static string GetString(JsonElement fields, string key, string subKey)
        {
            try
            {
                var el = fields.GetProperty(key);
                if (el.ValueKind == JsonValueKind.Null) return string.Empty;
                return el.GetProperty(subKey).GetString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string GetLabels(JsonElement fields)
        {
            try
            {
                var labels = fields.GetProperty("labels");
                var list = new List<string>();
                foreach (var l in labels.EnumerateArray())
                    list.Add(l.GetString() ?? "");
                return string.Join(", ", list);
            }
            catch { return string.Empty; }
        }

        private static DateTime? GetDate(JsonElement fields, string key)
        {
            try
            {
                var val = fields.GetProperty(key).GetString();
                return val != null ? DateTime.Parse(val) : null;
            }
            catch { return null; }
        }

        private static Models.TaskStatus MapJiraStatus(string status) => status.ToLower() switch
        {
            "done" or "closed" or "resolved" => Models.TaskStatus.Done,
            "in progress" or "in development" => Models.TaskStatus.InProgress,
            "in review" or "code review" or "testing" => Models.TaskStatus.InReview,
            "blocked" or "impediment" => Models.TaskStatus.Canceled,
            _ => Models.TaskStatus.Todo
        };

        private static TaskPriority MapJiraPriority(JsonElement fields)
        {
            try
            {
                var p = fields.GetProperty("priority").GetProperty("name").GetString()?.ToLower();
                return p switch
                {
                    "highest" or "critical" => TaskPriority.Critical,
                    "high" => TaskPriority.High,
                    "medium" => TaskPriority.Medium,
                    _ => TaskPriority.Low
                };
            }
            catch { return TaskPriority.Medium; }
        }
    }

    public class JiraProject
    {
        public string Id { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class JiraTransition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}