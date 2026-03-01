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

namespace QAssistant.Models
{
    public enum WebhookType { Slack, Teams, Generic }

    public class WebhookConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public WebhookType Type { get; set; } = WebhookType.Slack;
        public bool IsEnabled { get; set; } = true;
        public bool NotifyOnTestPlanFail { get; set; } = true;
        public bool NotifyOnHighPriorityDone { get; set; } = true;
        public bool NotifyOnDueDate { get; set; }
        public bool NotifyOnAiAnalysis { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
