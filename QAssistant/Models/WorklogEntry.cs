using System;

namespace QAssistant.Models
{
    public class WorklogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Author { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string FromValue { get; set; } = string.Empty;
        public string ToValue { get; set; } = string.Empty;
    }
}
