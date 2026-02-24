using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    public class JiraService
    {
        private readonly HttpClient _client = new();
        private readonly string _baseUrl;

        public JiraService(string domain, string email, string apiToken)
        {
            _baseUrl = $"https://{domain}.atlassian.net/rest/api/3";
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
            _client.DefaultRequestHeaders.Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<List<JiraProject>> GetProjectsAsync()
        {
            var response = await _client.GetStringAsync($"{_baseUrl}/project");
            var projects = new List<JiraProject>();

            using var doc = JsonDocument.Parse(response);
            foreach (var node in doc.RootElement.EnumerateArray())
            {
                projects.Add(new JiraProject
                {
                    Id = node.GetProperty("id").GetString() ?? "",
                    Key = node.GetProperty("key").GetString() ?? "",
                    Name = node.GetProperty("name").GetString() ?? ""
                });
            }

            return projects;
        }

        public async Task<List<ProjectTask>> GetIssuesAsync(string projectKey)
        {
            var jql = $"project={projectKey} ORDER BY updated DESC";
            var url = $"{_baseUrl}/search?jql={Uri.EscapeDataString(jql)}&maxResults=100&fields=summary,description,status,priority,assignee,reporter,issuetype,duedate,story_points,labels,components,comment";
            var response = await _client.GetStringAsync(url);
            var tasks = new List<ProjectTask>();

            using var doc = JsonDocument.Parse(response);
            var issues = doc.RootElement.GetProperty("issues");

            foreach (var issue in issues.EnumerateArray())
            {
                var fields = issue.GetProperty("fields");
                var task = new ProjectTask
                {
                    Id = Guid.NewGuid(),
                    ExternalId = issue.GetProperty("id").GetString() ?? "",
                    Title = fields.GetProperty("summary").GetString() ?? "",
                    Description = GetJiraDescription(fields),
                    Status = MapJiraStatus(fields.GetProperty("status")
                        .GetProperty("name").GetString() ?? ""),
                    Priority = MapJiraPriority(fields),
                    TicketUrl = $"https://{_baseUrl.Split('/')[2]}/browse/{issue.GetProperty("key").GetString()}",
                    Source = TaskSource.Jira,
                    IssueType = GetString(fields, "issuetype", "name"),
                    Assignee = GetString(fields, "assignee", "displayName"),
                    Reporter = GetString(fields, "reporter", "displayName"),
                    Labels = GetLabels(fields),
                    DueDate = GetDate(fields, "duedate")
                };
                tasks.Add(task);
            }

            return tasks;
        }

        public async Task UpdateIssueStatusAsync(string issueIdOrKey, string transitionId)
        {
            var url = $"{_baseUrl}/issue/{issueIdOrKey}/transitions";

            var payload = new System.Text.Json.Nodes.JsonObject
            {
                ["transition"] = new System.Text.Json.Nodes.JsonObject { ["id"] = transitionId }
            }.ToJsonString();

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _client.PostAsync(url, content);
        }

        public async Task AddCommentAsync(string issueIdOrKey, string body)
        {
            var url = $"{_baseUrl}/issue/{issueIdOrKey}/comment";

            var payload = new System.Text.Json.Nodes.JsonObject
            {
                ["body"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "doc",
                    ["version"] = 1,
                    ["content"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "paragraph",
                    ["content"] = new System.Text.Json.Nodes.JsonArray
                    {
                        new System.Text.Json.Nodes.JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = body
                        }
                    }
                }
            }
                }
            }.ToJsonString();

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _client.PostAsync(url, content);
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
            "blocked" or "impediment" => Models.TaskStatus.Blocked,
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