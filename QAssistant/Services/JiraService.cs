using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    public class JiraService(string domain, string email, string apiToken)
    {
        private readonly HttpClient _client = CreateClient(email, apiToken);
        private readonly string _baseUrl = $"https://{domain}.atlassian.net/rest/api/3";
        private readonly string _browseUrl = $"https://{domain}.atlassian.net/browse";

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
            var projects = await _client.GetFromJsonAsync<List<JsonElement>>($"{_baseUrl}/project");
            return projects?.Select(node => new JiraProject
            {
                Id = node.GetProperty("id").GetString() ?? "",
                Key = node.GetProperty("key").GetString() ?? "",
                Name = node.GetProperty("name").GetString() ?? ""
            }).ToList() ?? [];
        }

        public async Task<List<ProjectTask>> GetIssuesAsync(string projectKey)
        {
            var jql = $"project={projectKey} ORDER BY updated DESC";
            var url = $"{_baseUrl}/search?jql={Uri.EscapeDataString(jql)}&maxResults=100&fields=summary,description,status,priority,assignee,reporter,issuetype,duedate,story_points,labels,components,comment";

            var response = await _client.GetFromJsonAsync<JsonElement>(url);
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
            var url = $"{_baseUrl}/issue/{issueIdOrKey}/transitions";
            var payload = new { transition = new { id = transitionId } };
            await _client.PostAsJsonAsync(url, payload);
        }

        public async Task AddCommentAsync(string issueIdOrKey, string body)
        {
            var url = $"{_baseUrl}/issue/{issueIdOrKey}/comment";
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

            await _client.PostAsJsonAsync(url, payload);
        }

        public async Task<List<LinearComment>> GetCommentsAsync(string issueIdOrKey)
        {
            var url = $"{_baseUrl}/issue/{issueIdOrKey}/comment?orderBy=-created";
            var response = await _client.GetFromJsonAsync<JsonElement>(url);
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
            var url = $"{_baseUrl}/issue/{issueIdOrKey}/transitions";
            var response = await _client.GetFromJsonAsync<JsonElement>(url);
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
            var url = $"{_baseUrl}/issue/{issueIdOrKey}?expand=changelog&fields=summary";
            var response = await _client.GetFromJsonAsync<JsonElement>(url);
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

        private static string ExtractAdfText(JsonElement adfNode)
        {
            var sb = new StringBuilder();
            if (adfNode.TryGetProperty("content", out var content))
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("content", out var inline))
                    {
                        foreach (var item in inline.EnumerateArray())
                        {
                            if (item.TryGetProperty("text", out var text))
                                sb.Append(text.GetString());
                        }
                    }
                    sb.AppendLine();
                }
            }
            return sb.ToString().Trim();
        }

        private static string GetJiraDescription(JsonElement fields)
        {
            try
            {
                var desc = fields.GetProperty("description");
                if (desc.ValueKind == JsonValueKind.Null) return string.Empty;
                var content = desc.GetProperty("content");
                var sb = new StringBuilder();
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("content", out var inline))
                        foreach (var item in inline.EnumerateArray())
                            if (item.TryGetProperty("text", out var text))
                                sb.Append(text.GetString());
                    sb.AppendLine();
                }
                return sb.ToString().Trim();
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