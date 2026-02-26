using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    public class GeminiService(string apiKey)
    {
        private readonly HttpClient _client = CreateClient(apiKey);
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
        private const string FallbackModel = "models/gemini-3-flash";

        private static HttpClient CreateClient(string apiKey)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
            return client;
        }

        /// <summary>
        /// Builds a Token-Oriented Object Notation (TOON) prompt for issue analysis.

        /// TOON uses compact key-value pairs and directive-style instructions to
        /// significantly reduce token consumption while preserving semantic quality.
        /// </summary>
        public static string BuildToonPrompt(ProjectTask task, IReadOnlyList<LinearComment>? comments = null, int attachedImageCount = 0)
        {
            var sb = new StringBuilder();

            // Directives block — compact instruction set
            sb.AppendLine("@role:sr_qa_engineer");
            sb.AppendLine("@task:deep_issue_analysis");
            sb.AppendLine("@out_fmt:md_sections[## Root Cause Analysis,## Impact Assessment,## Suggested Fix,## Prevention Recommendations]");
            sb.AppendLine("@rules:all_sections_required|multi_sentence|specific_actionable|infer_if_brief|no_skip|no_merge");
            sb.AppendLine("---");

            // Issue data in TOON object notation
            sb.AppendLine("issue{");
            sb.AppendLine($" t:{task.Title}");

            if (!string.IsNullOrEmpty(task.IssueIdentifier))
                sb.AppendLine($" id:{task.IssueIdentifier}");

            sb.AppendLine($" status:{task.Status}");
            sb.AppendLine($" priority:{task.Priority}");

            if (!string.IsNullOrEmpty(task.Assignee))
                sb.AppendLine($" assignee:{task.Assignee}");

            if (!string.IsNullOrEmpty(task.Labels))
                sb.AppendLine($" labels:{task.Labels}");

            if (task.DueDate.HasValue)
                sb.AppendLine($" due:{task.DueDate.Value:yyyy-MM-dd}");

            sb.AppendLine($" desc:{(string.IsNullOrWhiteSpace(task.Description) ? "(none—infer from title+metadata)" : task.Description)}");
            sb.AppendLine("}");

            // Comments in TOON array notation
            if (comments is { Count: > 0 })
            {
                sb.AppendLine("comments[");
                foreach (var c in comments)
                    sb.AppendLine($" {{author:{c.AuthorName},date:{c.CreatedAt:yyyy-MM-dd},body:{c.Body}}}");
                sb.AppendLine("]");
            }

            if (attachedImageCount > 0)
                sb.AppendLine($"@media:{attachedImageCount}_image(s)_attached—analyze visual content for additional context (screenshots, error messages, UI state, logs)");

            return sb.ToString();
        }

        public async Task<List<(string, string[])>> ListModelsAsync()

        {
            var url = $"{BaseUrl}/models";
            var resp = await _client.GetAsync(url);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"ListModels failed ({(int)resp.StatusCode}): {json}");

            using var doc = JsonDocument.Parse(json);
            var list = new List<(string, string[])>();

            if (doc.RootElement.TryGetProperty("models", out var models))
            {
                foreach (var m in models.EnumerateArray())
                {
                    var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "<unknown>" : "<unknown>";

                    // The API may return supported generation method names under different property
                    // names depending on the server version. Prefer the newer property but fall back
                    // to older property name for compatibility.
                    string[] methods = Array.Empty<string>();
                    if (m.TryGetProperty("supportedGenerationMethods", out var sgm))
                    {
                        methods = sgm.EnumerateArray().Select(x => x.GetString() ?? string.Empty)
                                         .Where(x => !string.IsNullOrEmpty(x)).ToArray();
                    }
                    else if (m.TryGetProperty("supportedMethods", out var sm))
                    {
                        methods = sm.EnumerateArray().Select(x => x.GetString() ?? string.Empty)
                                        .Where(x => !string.IsNullOrEmpty(x)).ToArray();
                    }
                    list.Add((name, methods));
                }

                return list;
            }

            throw new Exception("ListModels returned unexpected response: " + json);
        }

        private static readonly string[] CandidateGenerateMethods = new[] { "generateContent", "generateMessage", "generateText" };

        private async Task<string?> FindModelSupportingGenerateContentAsync()
        {
            // Backwards-compatible wrapper that prefers known method names but falls back to any 'generate' containing method.
            var models = await ListModelsAsync();

            foreach (var method in CandidateGenerateMethods)
            {
                var found = models.FirstOrDefault(m => m.Item2.Any(s => string.Equals(s, method, StringComparison.OrdinalIgnoreCase)));
                if (!string.IsNullOrEmpty(found.Item1))
                    return found.Item1;
            }

            // Fallback: pick first model that exposes any method containing the word 'generate'
            var fallback = models.FirstOrDefault(m => m.Item2.Any(s => s.IndexOf("generate", StringComparison.OrdinalIgnoreCase) >= 0));
            return fallback.Item1;
        }

        public async Task<string> AnalyzeIssueAsync(string prompt, IReadOnlyList<(string MimeType, string Base64Data)>? images = null, string? modelName = null)
        {
            if (string.IsNullOrEmpty(modelName))
            {
                modelName = await FindModelSupportingGenerateContentAsync();
                if (string.IsNullOrEmpty(modelName))
                {
                    var modelsJson = await _client.GetStringAsync($"{BaseUrl}/models");
                    throw new Exception("No model supporting a generate-like method found. Call ListModelsAsync to inspect available models. Response: " + modelsJson);
                }
            }

            var primaryModel = NormalizeModelPath(modelName!);
            var fallbackModel = string.Equals(primaryModel, FallbackModel, StringComparison.OrdinalIgnoreCase)
                ? NormalizeModelPath(modelName!)
                : FallbackModel;

            // Try the primary model first
            try
            {
                return await SendGenerateRequestAsync(prompt, primaryModel, images);
            }
            catch (GeminiRateLimitException firstEx)
            {
                Debug.WriteLine($"Rate limit hit on {primaryModel}, falling back to {fallbackModel}: {firstEx.Message}");

                // Try the other model
                try
                {
                    return await SendGenerateRequestAsync(prompt, fallbackModel, images);
                }
                catch (GeminiRateLimitException secondEx)
                {
                    // Both models are rate-limited
                    throw new GeminiAllModelsRateLimitedException(primaryModel, fallbackModel, secondEx);
                }
            }
        }

        private static string NormalizeModelPath(string modelName) =>
            modelName.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? modelName
                : $"models/{modelName}";

        private static bool IsRateLimitError(HttpStatusCode statusCode, string responseBody)
        {
            // HTTP 429 is the standard rate-limit status code
            if (statusCode == HttpStatusCode.TooManyRequests)
                return true;

            // The Gemini API may also return 200 with an error body, or 400/403 with
            // quota-related messages. Check for known rate-limit indicators.
            if (string.IsNullOrEmpty(responseBody))
                return false;

            var bodyLower = responseBody.ToLowerInvariant();
            return bodyLower.Contains("rate limit")
                || bodyLower.Contains("resource_exhausted")
                || bodyLower.Contains("quota")
                || bodyLower.Contains("requests per minute")
                || bodyLower.Contains("tokens per minute")
                || bodyLower.Contains("requests per day");
        }

        private async Task<string> SendGenerateRequestAsync(string prompt, string modelName, IReadOnlyList<(string MimeType, string Base64Data)>? images = null)
        {
            var modelPath = NormalizeModelPath(modelName);

            var endpoint = $"{BaseUrl}/{modelPath}:generateContent";
            var url = endpoint;

            // Build JSON manually to avoid reflection-based serialization issues with trimming
            // Escape the prompt string properly for JSON
            var escapedPrompt = prompt
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");

            var jsonBuilder = new StringBuilder();
            jsonBuilder.AppendLine("{");
            jsonBuilder.AppendLine("  \"contents\": [");
            jsonBuilder.AppendLine("    {");
            jsonBuilder.AppendLine("      \"parts\": [");
            jsonBuilder.Append($"        {{\"text\": \"{escapedPrompt}\"}}");

            if (images is { Count: > 0 })
            {
                foreach (var (mimeType, base64Data) in images)
                {
                    jsonBuilder.AppendLine(",");
                    jsonBuilder.Append($"        {{\"inline_data\": {{\"mime_type\": \"{mimeType}\", \"data\": \"{base64Data}\"}}}}");
                }
            }

            jsonBuilder.AppendLine();
            jsonBuilder.AppendLine("      ]");
            jsonBuilder.AppendLine("    }");
            jsonBuilder.AppendLine("  ],");
            jsonBuilder.AppendLine("  \"generationConfig\": {");
            jsonBuilder.AppendLine("    \"temperature\": 0.3,");
            jsonBuilder.AppendLine("    \"maxOutputTokens\": 8192");
            jsonBuilder.AppendLine("  }");
            jsonBuilder.Append("}");

            var json = jsonBuilder.ToString();

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content);
            var responseStr = await response.Content.ReadAsStringAsync();

            if (IsRateLimitError(response.StatusCode, responseStr))
                throw new GeminiRateLimitException(modelPath, responseStr);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"API error ({(int)response.StatusCode}): {responseStr}");

            using var doc = JsonDocument.Parse(responseStr);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var errorMessage = error.GetProperty("message").GetString() ?? string.Empty;
                if (IsRateLimitError(HttpStatusCode.OK, errorMessage))
                    throw new GeminiRateLimitException(modelPath, errorMessage);

                throw new Exception(errorMessage);
            }

            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var contentEl)
                    && contentEl.TryGetProperty("parts", out var parts)
                    && parts.GetArrayLength() > 0
                    && parts[0].TryGetProperty("text", out var textEl))
                {
                    return textEl.GetString() ?? "No response received.";
                }
            }

            return "No response received.";
        }
    }

    /// <summary>
    /// Thrown when the Gemini API returns a rate-limit or quota-exhaustion response
    /// (requests per minute, tokens per minute, or requests per day).
    /// </summary>
    public class GeminiRateLimitException : Exception
    {
        public string Model { get; }

        public GeminiRateLimitException(string model, string responseBody)
            : base($"Rate limit exceeded for {model}: {responseBody}")
        {
            Model = model;
        }
    }

    /// <summary>
    /// Thrown when both the primary and fallback Gemini models are rate-limited.
    /// </summary>
    public class GeminiAllModelsRateLimitedException : Exception
    {
        public string PrimaryModel { get; }
        public string FallbackModel { get; }

        public GeminiAllModelsRateLimitedException(string primaryModel, string fallbackModel, Exception inner)
            : base($"All Gemini models rate-limited ({primaryModel}, {fallbackModel}). Please try again later.", inner)
        {
            PrimaryModel = primaryModel;
            FallbackModel = fallbackModel;
        }
    }
}