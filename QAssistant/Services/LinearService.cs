using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    public class LinearService
    {
        private readonly HttpClient _client = new();
        private const string Endpoint = "https://api.linear.app/graphql";

        public LinearService(string apiKey)
        {
            _client.DefaultRequestHeaders.Add("Authorization", apiKey);
            _client.DefaultRequestHeaders.Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
                    throw new Exception($"Unexpected response: {response}");

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
                        Source = TaskSource.Linear
                    };
                    tasks.Add(task);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Parse error: {ex.Message} | Raw: {response}");
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
                    throw new Exception($"Unexpected response: {response}");

                if (!data.TryGetProperty("teams", out var teamsEl))
                    throw new Exception($"No teams in response: {response}");

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
                throw new Exception($"Parse error: {ex.Message} | Raw: {response}");
            }

            return teams;
        }

        public async Task UpdateIssueStatusAsync(string issueId, string stateId)
        {
            var mutation = $@"
            mutation {{
                issueUpdate(id: ""{issueId}"", input: {{ stateId: ""{stateId}"" }}) {{
                    success
                }}
            }}";
            await PostQueryAsync(mutation);
        }

        public async Task<List<LinearComment>> GetCommentsAsync(string issueId)
        {
            var query = $@"
    {{
        issue(id: ""{issueId}"") {{
            comments {{
                nodes {{
                    body
                    createdAt
                    user {{ name }}
                }}
            }}
        }}
    }}";

            var response = await PostQueryAsync(query);
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
            var mutation = $@"
            mutation {{
                commentCreate(input: {{ issueId: ""{issueId}"", body: ""{body}"" }}) {{
                    success
                }}
            }}";
            await PostQueryAsync(mutation);
        }

        private async Task<string> PostQueryAsync(string query)
        {
            var payload = new System.Text.Json.Nodes.JsonObject { ["query"] = query }.ToJsonString();

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(Endpoint, content);
            return await response.Content.ReadAsStringAsync();
        }

        private static string CleanDescription(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            // Remove markdown images — handled separately via ExtractMediaUrls
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"!\[.*?\]\(.*?\)", "[image]");
            // Remove markdown links [text](url) -> text
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            // Remove HTML tags
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"<[^>]+>", "");
            // Remove markdown headers
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"#{1,6}\s", "");
            // Remove bold/italic
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\*{1,3}([^\*]+)\*{1,3}", "$1");
            // Remove code blocks
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"```[\s\S]*?```", "[code block]");
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"`([^`]+)`", "$1");
            // Trim excess whitespace
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\n{3,}", "\n\n").Trim();

            return raw;
        }

        public static List<string> ExtractMediaUrls(string? raw)
        {
            var urls = new List<string>();
            if (string.IsNullOrEmpty(raw)) return urls;

            // Extract markdown images ![alt](url)
            var imageMatches = System.Text.RegularExpressions.Regex.Matches(raw, @"!\[.*?\]\((.*?)\)");
            foreach (System.Text.RegularExpressions.Match match in imageMatches)
                urls.Add(match.Groups[1].Value);

            // Extract HTML img src
            var htmlMatches = System.Text.RegularExpressions.Regex.Matches(raw, @"<img[^>]+src=""([^""]+)""");
            foreach (System.Text.RegularExpressions.Match match in htmlMatches)
                urls.Add(match.Groups[1].Value);

            return urls;
        }

        private static Models.TaskStatus MapLinearStatus(string state) => state.ToLower() switch
        {
            "done" or "completed" => Models.TaskStatus.Done,
            "in progress" or "started" => Models.TaskStatus.InProgress,
            "in review" => Models.TaskStatus.InReview,
            "cancelled" or "blocked" => Models.TaskStatus.Blocked,
            _ => Models.TaskStatus.Todo
        };

        private static TaskPriority MapLinearPriority(int priority) => priority switch
        {
            1 => TaskPriority.Critical,
            2 => TaskPriority.High,
            3 => TaskPriority.Medium,
            _ => TaskPriority.Low
        };
    }

    public class LinearTeam
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}