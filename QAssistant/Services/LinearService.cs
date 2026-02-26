using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    public class LinearService(string apiKey)
    {
        private readonly HttpClient _client = CreateClient(apiKey);
        private const string Endpoint = "https://api.linear.app/graphql";

        private static readonly Regex s_markdownImageRegex = new(@"!\[.*?\]\(.*?\)", RegexOptions.Compiled);
        private static readonly Regex s_markdownLinkRegex = new(@"\[([^\]]+)\]\([^\)]+\)", RegexOptions.Compiled);
        private static readonly Regex s_htmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex s_markdownHeaderRegex = new(@"#{1,6}\s", RegexOptions.Compiled);
        private static readonly Regex s_boldItalicRegex = new(@"\*{1,3}([^\*]+)\*{1,3}", RegexOptions.Compiled);
        private static readonly Regex s_codeBlockRegex = new(@"```[\s\S]*?```", RegexOptions.Compiled);
        private static readonly Regex s_inlineCodeRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex s_excessNewlinesRegex = new(@"\n{3,}", RegexOptions.Compiled);
        private static readonly Regex s_extractMarkdownImageUrlRegex = new(@"!\[.*?\]\((.*?)\)", RegexOptions.Compiled);
        private static readonly Regex s_extractHtmlImgSrcRegex = new(@"<img[^>]+src=""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex s_extractPlainImageUrlRegex = new(@"https?://[^\s\)\]""]+\.(?:png|jpe?g|gif|webp|svg|bmp|mp4|webm|mov)(?:\?[^\s\)\]""]*)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static HttpClient CreateClient(string apiKey)

        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", apiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }


        public async Task<List<ProjectTask>> GetIssuesAsync(string teamId)
        {
            var query = @"
    {
        issues(first: 50) {
            nodes {
                id
                identifier
                title
                description
                priority
                state { name }
                assignee { name }
                dueDate
                url
                labels { nodes { name } }
                attachments { nodes { url title } }
            }
        }
    }";

            var response = await PostQueryAsync(query);
            var tasks = new List<ProjectTask>();

            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("errors", out var errors))
                    throw new Exception(errors[0].GetProperty("message").GetString());

                if (!root.TryGetProperty("data", out var data))
                    throw new Exception("Unexpected response from Linear API.");

                var nodes = data.GetProperty("issues").GetProperty("nodes");

                foreach (var node in nodes.EnumerateArray())
                {
                    var stateName = node.GetProperty("state").GetProperty("name").GetString() ?? "";

                    // Parse assignee
                    string assignee = "";
                    if (node.TryGetProperty("assignee", out var assigneeEl) &&
                        assigneeEl.ValueKind != JsonValueKind.Null)
                        assignee = assigneeEl.GetProperty("name").GetString() ?? "";

                    // Parse due date
                    DateTime? dueDate = null;
                    if (node.TryGetProperty("dueDate", out var dueDateEl) &&
                        dueDateEl.ValueKind != JsonValueKind.Null &&
                        DateTime.TryParse(dueDateEl.GetString(), out var parsedDate))
                        dueDate = parsedDate;

                    // Parse labels
                    string labels = "";
                    if (node.TryGetProperty("labels", out var labelsEl) &&
                        labelsEl.ValueKind != JsonValueKind.Null &&
                        labelsEl.TryGetProperty("nodes", out var labelNodes))
                    {
                        var labelList = new List<string>();
                        foreach (var label in labelNodes.EnumerateArray())
                            labelList.Add(label.GetProperty("name").GetString() ?? "");
                        labels = string.Join(", ", labelList);
                    }

                    // Parse attachment URLs
                    var attachmentUrls = new List<string>();
                    if (node.TryGetProperty("attachments", out var attachmentsEl) &&
                        attachmentsEl.ValueKind != JsonValueKind.Null &&
                        attachmentsEl.TryGetProperty("nodes", out var attachmentNodes))
                    {
                        foreach (var att in attachmentNodes.EnumerateArray())
                        {
                            if (att.TryGetProperty("url", out var attUrlEl) &&
                                attUrlEl.ValueKind != JsonValueKind.Null)
                            {
                                var attUrl = attUrlEl.GetString();
                                if (!string.IsNullOrEmpty(attUrl))
                                    attachmentUrls.Add(attUrl);
                            }
                        }
                    }

                    var task = new ProjectTask
                    {
                        Id = Guid.NewGuid(),
                        ExternalId = node.GetProperty("id").GetString() ?? "",
                        IssueIdentifier = node.TryGetProperty("identifier", out var identifier)
                            ? identifier.GetString() ?? "" : "",
                        Title = node.GetProperty("title").GetString() ?? "",
                        Description = CleanDescription(node.GetProperty("description").GetString()),
                        Status = MapLinearStatus(stateName),
                        Priority = MapLinearPriority(node.GetProperty("priority").GetInt32()),
                        TicketUrl = node.GetProperty("url").GetString() ?? "",
                        RawDescription = node.GetProperty("description").GetString() ?? "",
                        Assignee = assignee,
                        Labels = labels,
                        DueDate = dueDate,
                        Source = TaskSource.Linear,
                        AttachmentUrls = attachmentUrls
                    };
                    tasks.Add(task);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Parse error while reading Linear issues response: {ex.Message}");
            }

            return tasks;
        }

        public async Task<List<LinearTeam>> GetTeamsAsync()
        {
            var query = "{ teams { nodes { id name } } }";
            var response = await PostQueryAsync(query);
            var teams = new List<LinearTeam>();

            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("errors", out var errors))
                    throw new Exception(errors[0].GetProperty("message").GetString());

                if (!root.TryGetProperty("data", out var data))
                    throw new Exception("Unexpected response from Linear API.");

                if (!data.TryGetProperty("teams", out var teamsEl))
                    throw new Exception("No teams found in Linear API response.");

                var nodes = teamsEl.GetProperty("nodes");
                foreach (var node in nodes.EnumerateArray())
                {
                    teams.Add(new LinearTeam
                    {
                        Id = node.GetProperty("id").GetString() ?? "",
                        Name = node.GetProperty("name").GetString() ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Parse error while reading Linear teams response: {ex.Message}");
            }

            return teams;
        }

        public async Task UpdateIssueStatusAsync(string issueId, string stateId)
        {
            var mutation = @"
            mutation($issueId: String!, $stateId: String!) {
                issueUpdate(id: $issueId, input: { stateId: $stateId }) {
                    success
                }
            }";
            var response = await PostQueryAsync(mutation, new JsonObject
            {
                ["issueId"] = issueId,
                ["stateId"] = stateId
            });

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            {
                var msg = errors[0].TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Unknown error";
                throw new Exception($"Linear API error: {msg}");
            }

            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("issueUpdate", out var issueUpdate) &&
                issueUpdate.TryGetProperty("success", out var success) &&
                !success.GetBoolean())
            {
                throw new Exception("Linear rejected the status update.");
            }
        }

        public async Task<List<LinearComment>> GetCommentsAsync(string issueId)
        {
            var query = @"
    query($issueId: String!) {
        issue(id: $issueId) {
            comments {
                nodes {
                    body
                    createdAt
                    user { name }
                }
            }
        }
    }";

            var response = await PostQueryAsync(query, new JsonObject
            {
                ["issueId"] = issueId
            });
            var comments = new List<LinearComment>();

            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (!root.TryGetProperty("data", out var data)) return comments;
                if (!data.TryGetProperty("issue", out var issue)) return comments;
                if (!issue.TryGetProperty("comments", out var commentsEl)) return comments;

                var nodes = commentsEl.GetProperty("nodes");
                foreach (var node in nodes.EnumerateArray())
                {
                    string author = "";
                    if (node.TryGetProperty("user", out var user) &&
                        user.ValueKind != JsonValueKind.Null)
                        author = user.GetProperty("name").GetString() ?? "";

                    DateTime createdAt = DateTime.Now;
                    if (node.TryGetProperty("createdAt", out var createdAtEl))
                        DateTime.TryParse(createdAtEl.GetString(), out createdAt);

                    comments.Add(new LinearComment
                    {
                        Body = CleanDescription(node.GetProperty("body").GetString()),
                        AuthorName = author,
                        CreatedAt = createdAt
                    });
                }
            }
            catch { }

            return comments;
        }

        public async Task AddCommentAsync(string issueId, string body)
        {
            var mutation = @"
            mutation($issueId: String!, $body: String!) {
                commentCreate(input: { issueId: $issueId, body: $body }) {
                    success
                }
            }";
            await PostQueryAsync(mutation, new JsonObject
            {
                ["issueId"] = issueId,
                ["body"] = body
            });
        }

        private async Task<string> PostQueryAsync(string query, JsonObject? variables = null)
        {
            var payload = new JsonObject { ["query"] = query };
            if (variables != null)
                payload["variables"] = variables;

            var response = await _client.PostAsJsonAsync(Endpoint, payload);
            return await response.Content.ReadAsStringAsync();
        }


        private static string CleanDescription(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            raw = s_markdownImageRegex.Replace(raw, "");
            raw = s_markdownLinkRegex.Replace(raw, "$1");
            raw = s_htmlTagRegex.Replace(raw, "");
            raw = s_extractPlainImageUrlRegex.Replace(raw, "");
            raw = s_markdownHeaderRegex.Replace(raw, "");
            raw = s_boldItalicRegex.Replace(raw, "$1");
            raw = s_codeBlockRegex.Replace(raw, "[code block]");
            raw = s_inlineCodeRegex.Replace(raw, "$1");
            raw = s_excessNewlinesRegex.Replace(raw, "\n\n").Trim();

            return raw;
        }

        public static List<string> ExtractMediaUrls(string? raw)
        {
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return urls.ToList();

            foreach (Match match in s_extractMarkdownImageUrlRegex.Matches(raw))
                urls.Add(match.Groups[1].Value);

            foreach (Match match in s_extractHtmlImgSrcRegex.Matches(raw))
                urls.Add(match.Groups[1].Value);

            foreach (Match match in s_extractPlainImageUrlRegex.Matches(raw))
                urls.Add(match.Value);

            return urls.ToList();
        }

        private static Models.TaskStatus MapLinearStatus(string state) => state.ToLower() switch
        {
            "backlog" or "triage" or "unstarted" => Models.TaskStatus.Backlog,
            "todo" => Models.TaskStatus.Todo,
            "in progress" or "started" => Models.TaskStatus.InProgress,
            "in review" or "review" or "qa" => Models.TaskStatus.InReview,
            "done" or "completed" or "closed" => Models.TaskStatus.Done,
            "canceled" or "cancelled" => Models.TaskStatus.Canceled,
            "duplicate" => Models.TaskStatus.Duplicate,
            _ => Models.TaskStatus.Backlog
        };

        private static TaskPriority MapLinearPriority(int priority) => priority switch
        {
            1 => TaskPriority.Critical,
            2 => TaskPriority.High,
            3 => TaskPriority.Medium,
            _ => TaskPriority.Low
        };

        public async Task<List<LinearWorkflowState>> GetWorkflowStatesAsync()
        {
            var query = @"
    {
        workflowStates(first: 200) {
            nodes {
                id
                name
            }
        }
    }";

            var response = await PostQueryAsync(query);
            var states = new List<LinearWorkflowState>();

            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (!root.TryGetProperty("data", out var data)) return states;
                if (!data.TryGetProperty("workflowStates", out var statesEl)) return states;

                var nodes = statesEl.GetProperty("nodes");
                foreach (var node in nodes.EnumerateArray())
                {
                    states.Add(new LinearWorkflowState
                    {
                        Id = node.GetProperty("id").GetString() ?? "",
                        Name = node.GetProperty("name").GetString() ?? ""
                    });
                }
            }
            catch { }

            return states;
        }

        public static string? FindMatchingStateId(Models.TaskStatus targetStatus, List<LinearWorkflowState> states)
        {
            if (states.Count == 0) return null;

            // First pass: find a state whose name maps back to the target status via MapLinearStatus.
            // This reuses the exact same inbound mapping logic, so it handles custom names
            // like "Backlog", "Started", "Completed", "Cancelled", etc.
            foreach (var state in states)
            {
                if (MapLinearStatus(state.Name) == targetStatus)
                    return state.Id;
            }

            // Second pass: try common default names as a fallback
            var candidateNames = targetStatus switch
            {
                Models.TaskStatus.Backlog => new[] { "Backlog", "Triage", "Unstarted" },
                Models.TaskStatus.Todo => new[] { "Todo" },
                Models.TaskStatus.InProgress => new[] { "In Progress", "Started", "Doing" },
                Models.TaskStatus.InReview => new[] { "In Review", "Review", "QA" },
                Models.TaskStatus.Done => new[] { "Done", "Completed", "Closed" },
                Models.TaskStatus.Canceled => new[] { "Canceled", "Cancelled" },
                Models.TaskStatus.Duplicate => new[] { "Duplicate" },
                _ => Array.Empty<string>()
            };

            foreach (var candidate in candidateNames)
            {
                var match = states.FirstOrDefault(s =>
                    s.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Id;
            }

            return null;
        }
    }

    public class LinearTeam
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class LinearWorkflowState
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}