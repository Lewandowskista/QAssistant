using System;
using System.Collections.Generic;

namespace QAssistant.Models
{
    public class Note
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = "Untitled Note";
        public string Content { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public List<FileAttachment> Attachments { get; set; } = new();
    }
}