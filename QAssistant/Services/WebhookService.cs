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
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    /// <summary>
    /// Sends notifications to Slack / Microsoft Teams webhooks.
    /// </summary>
    public static class WebhookService
    {
        private static readonly HttpClient s_client = new() { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task SendAsync(WebhookConfig webhook, string title, string message, string? color = null)
        {
            if (!webhook.IsEnabled || string.IsNullOrWhiteSpace(webhook.Url))
                return;

            if (!Helpers.UriSecurity.IsHttpUrl(webhook.Url))
                return;

            try
            {
                string payload = webhook.Type switch
                {
                    WebhookType.Slack => BuildSlackPayload(title, message, color ?? "#A78BFA"),
                    WebhookType.Teams => BuildTeamsPayload(title, message, color ?? "#A78BFA"),
                    _ => BuildGenericPayload(title, message)
                };

                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                await s_client.PostAsync(webhook.Url, content);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebhookService.SendAsync error: {ex.Message}");
            }
        }

        public static async Task NotifyTestPlanResultAsync(IEnumerable<WebhookConfig> webhooks, string projectName, string planName, int passed, int failed, int total)
        {
            if (total == 0) return;

            double rate = (double)passed / total * 100;
            string emoji = rate >= 80 ? "✅" : rate >= 50 ? "⚠️" : "❌";
            string color = rate >= 80 ? "#10B981" : rate >= 50 ? "#F59E0B" : "#EF4444";

            string title = $"{emoji} Test Plan Complete – {planName}";
            string message = $"*Project:* {projectName}\n*Plan:* {planName}\n*Result:* {passed}/{total} passed ({rate:F0}%)";

            var tasks = new List<Task>();
            foreach (var wh in webhooks)
                tasks.Add(SendAsync(wh, title, message, color));
            await Task.WhenAll(tasks);
        }

        public static async Task NotifyHighPriorityDoneAsync(IEnumerable<WebhookConfig> webhooks, string projectName, string taskTitle)
        {
            string title = "🎯 High-Priority Task Done";
            string message = $"*Project:* {projectName}\n*Task:* {taskTitle}";

            var tasks = new List<Task>();
            foreach (var wh in webhooks)
                if (wh.NotifyOnHighPriorityDone)
                    tasks.Add(SendAsync(wh, title, message, "#10B981"));
            await Task.WhenAll(tasks);
        }

        public static async Task NotifyAiAnalysisAsync(IEnumerable<WebhookConfig> webhooks, string projectName, string taskTitle, string summary)
        {
            string title = "🤖 AI Analysis Complete";
            string message = $"*Project:* {projectName}\n*Task:* {taskTitle}\n{summary}";

            var tasks = new List<Task>();
            foreach (var wh in webhooks)
                if (wh.NotifyOnAiAnalysis)
                    tasks.Add(SendAsync(wh, title, message, "#A78BFA"));
            await Task.WhenAll(tasks);
        }

        // ── Payload builders ─────────────────────────────────────────

        private static string BuildSlackPayload(string title, string message, string color)
        {
            var payload = new
            {
                attachments = new[]
                {
                    new
                    {
                        color,
                        title,
                        text = message,
                        footer = "QAssistant",
                        ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }
                }
            };
            return JsonSerializer.Serialize(payload);
        }

        private static string BuildTeamsPayload(string title, string message, string color)
        {
            // Teams Adaptive Card via Incoming Webhook
            var payload = new
            {
                type = "message",
                attachments = new[]
                {
                    new
                    {
                        contentType = "application/vnd.microsoft.card.adaptive",
                        content = new
                        {
                            type = "AdaptiveCard",
                            version = "1.4",
                            body = new object[]
                            {
                                new { type = "TextBlock", size = "Medium", weight = "Bolder", text = title },
                                new { type = "TextBlock", text = message, wrap = true }
                            }
                        }
                    }
                }
            };
            return JsonSerializer.Serialize(payload);
        }

        private static string BuildGenericPayload(string title, string message)
        {
            return JsonSerializer.Serialize(new { title, message, timestamp = DateTime.UtcNow });
        }
    }
}
