using System;

namespace QAssistant.Models
{
    public class LinearComment
    {
        public string Body { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}