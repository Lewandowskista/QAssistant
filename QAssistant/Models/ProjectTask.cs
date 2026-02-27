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

namespace QAssistant.Models
{
    public enum TaskStatus { Backlog, Todo, InProgress, InReview, Done, Canceled, Duplicate }
    public enum TaskPriority { Low, Medium, High, Critical }
    public enum TaskSource { Manual, Linear, Jira }

    public class ProjectTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Title { get; set; }
        public string RawDescription { get; set; } = string.Empty;
        public string IssueIdentifier { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TaskStatus Status { get; set; } = TaskStatus.Todo;
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
        public DateTime? DueDate { get; set; }
        public string? TicketUrl { get; set; }

        public string? ExternalId { get; set; }
        public TaskSource Source { get; set; } = TaskSource.Manual;
        public string? IssueType { get; set; }
        public string? Assignee { get; set; }
        public string? Reporter { get; set; }
        public string? Labels { get; set; }
        public List<string> AttachmentUrls { get; set; } = [];
        public List<AnalysisEntry> AnalysisHistory { get; set; } = [];
    }
}
