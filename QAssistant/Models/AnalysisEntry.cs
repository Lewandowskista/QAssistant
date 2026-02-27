using System;
using System.Security.Cryptography;
using System.Text;

namespace QAssistant.Models
{
    public class AnalysisEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int Version { get; set; }
        public string Hash { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string TaskStatus { get; set; } = string.Empty;
        public string TaskPriority { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string FullResult { get; set; } = string.Empty;

        public static string ComputeHash(string content)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexStringLower(bytes)[..7];
        }
    }
}
