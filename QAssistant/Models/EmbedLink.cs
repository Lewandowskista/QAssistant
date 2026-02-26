using System;

namespace QAssistant.Models
{
    public enum LinkType { Notion, Figma, Linear, GitHub, Generic }

    public class EmbedLink
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public LinkType Type { get; set; } = LinkType.Generic;
        public bool IsPinned { get; set; }
    }
}