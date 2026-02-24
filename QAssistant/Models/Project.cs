using System;
using System.Collections.Generic;

namespace QAssistant.Models
{
    public class Project
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Color { get; set; } = "#A78BFA";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<Note> Notes { get; set; } = new();
        public List<ProjectTask> Tasks { get; set; } = new();
        public List<EmbedLink> Links { get; set; } = new();
        public List<FileAttachment> Attachments { get; set; } = new();
    }
}