namespace QAssistant.Models
{
    public class SearchResult
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public SearchResultType Type { get; set; }
        public object? Item { get; set; }
        public Project? Project { get; set; }

        public override string ToString() => $"{Title} — {ProjectName}";
    }

    public enum SearchResultType { Note, Task }
}