using System;

namespace DesktopApp.Models
{
    public enum TaskStatus { Todo, InProgress, InReview, Done, Blocked }
    public enum TaskPriority { Low, Medium, High, Critical }
    public enum TaskSource { Manual, Linear, Jira }

    public class ProjectTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string IssueIdentifier { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TaskStatus Status { get; set; } = TaskStatus.Todo;
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
        public DateTime? DueDate { get; set; }
        public string? TicketUrl { get; set; }

        // Integration fields
        public string? ExternalId { get; set; }
        public TaskSource Source { get; set; } = TaskSource.Manual;
        public string? IssueType { get; set; }
        public string? Assignee { get; set; }
        public string? Reporter { get; set; }
        public string? Labels { get; set; }
    }
}