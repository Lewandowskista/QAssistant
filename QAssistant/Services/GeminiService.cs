using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace QAssistant.Services
{
    public class GeminiService
    {
        private readonly HttpClient _client = new();
        private readonly string _apiKey;
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        public GeminiService(string apiKey)
        {
            _apiKey = apiKey;
            _client.DefaultRequestHeaders.Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<List<(string, string[])>> ListModelsAsync()
        {
            var url = $"{BaseUrl}/models?key={_apiKey}";
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

        public async Task<string> AnalyzeIssueAsync(string prompt, string? modelName = null)
        {
            if (string.IsNullOrEmpty(modelName))
            {
                modelName = await FindModelSupportingGenerateContentAsync();
                if (string.IsNullOrEmpty(modelName))
                {
                    // Include the raw models response to aid debugging (ListModelsAsync already parses and would throw on unexpected responses).
                    var modelsJson = await _client.GetStringAsync($"{BaseUrl}/models?key={_apiKey}");
                    throw new Exception("No model supporting a generate-like method found. Call ListModelsAsync to inspect available models. Response: " + modelsJson);
                }
            }

            var chosenModel = modelName!;

            // Ensure we don't produce a duplicate 'models/' segment if the model name already
            // includes the prefix returned by the API (e.g. "models/gemini-2.5-flash").
            var modelPath = chosenModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? chosenModel
                : $"models/{chosenModel}";

            var endpoint = $"{BaseUrl}/{modelPath}:generateContent";
            var url = $"{endpoint}?key={_apiKey}";

            // Build JSON manually to avoid reflection-based serialization issues with trimming
            var json = $$"""
            {
              "contents": [
                {
                  "parts": [
                    {
                      "text": {{JsonSerializer.Serialize(prompt)}}
                    }
                  ]
                }
              ],
              "generationConfig": {
                "temperature": 0.3,
                "maxOutputTokens": 2048
              }
            }
            """;

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content);
            var responseStr = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"API error ({(int)response.StatusCode}): {responseStr}");

            using var doc = JsonDocument.Parse(responseStr);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
                throw new Exception(error.GetProperty("message").GetString());

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
}