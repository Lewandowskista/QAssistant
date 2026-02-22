using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopApp.Models;

namespace DesktopApp.Services
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
                title
                description
                priority
                state { name }
                assignee { name }
                dueDate
                url
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
                    var task = new ProjectTask
                    {
                        Id = Guid.NewGuid(),
                        Title = node.GetProperty("title").GetString() ?? "",
                        Description = node.GetProperty("description").GetString() ?? "",
                        Status = MapLinearStatus(stateName),
                        Priority = MapLinearPriority(node.GetProperty("priority").GetInt32()),
                        TicketUrl = node.GetProperty("url").GetString() ?? "",
                        ExternalId = node.GetProperty("id").GetString() ?? "",
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

                // Check for errors in the response
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
            var payload = JsonSerializer.Serialize(new { query });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(Endpoint, content);
            return await response.Content.ReadAsStringAsync();
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