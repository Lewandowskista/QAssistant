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
    }
}
