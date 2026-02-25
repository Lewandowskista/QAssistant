using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media;

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

        [JsonIgnore]
        public SolidColorBrush ColorBrush
        {
            get
            {
                try
                {
                    var hex = Color.StartsWith("#") ? Color[1..] : Color;
                    if (hex.Length == 6)
                    {
                        byte r = Convert.ToByte(hex[0..2], 16);
                        byte g = Convert.ToByte(hex[2..4], 16);
                        byte b = Convert.ToByte(hex[4..6], 16);
                        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
                    }
                }
                catch { }
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250));
            }
        }

        // Display name when no DataTemplate is present
        public override string ToString() => Name;
    }
}