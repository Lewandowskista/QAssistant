// Copyright (C) 2026 Lewandowskista
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

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
        public string ClientName { get; set; } = string.Empty;

        private string _color = "#A78BFA";
        public string Color
        {
            get => _color;
            set
            {
                _color = value;
                _cachedBrush = null;
            }
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<Note> Notes { get; set; } = new();
        public List<ProjectTask> Tasks { get; set; } = new();
        public List<EmbedLink> Links { get; set; } = new();
        public List<FileAttachment> Attachments { get; set; } = new();
        public List<TestCase> TestCases { get; set; } = new();
        public List<TestPlan> TestPlans { get; set; } = new();
        public List<TestExecution> TestExecutions { get; set; } = new();
        public List<SavedApiRequest> ApiRequests { get; set; } = new();
        public List<ChecklistTemplate> Checklists { get; set; } = new();
        public List<QaEnvironment> Environments { get; set; } = new();
        public List<TestDataGroup> TestDataGroups { get; set; } = new();

        /// <summary>
        /// Persisted analysis history for Linear tasks, keyed by ExternalId.
        /// Linear issues are fetched fresh each session, so their AnalysisHistory
        /// is stored here and merged back after fetching.
        /// </summary>
        public Dictionary<string, List<AnalysisEntry>> LinearAnalysisHistory { get; set; } = new();

        /// <summary>
        /// Persisted analysis history for Jira tasks, keyed by ExternalId.
        /// </summary>
        public Dictionary<string, List<AnalysisEntry>> JiraAnalysisHistory { get; set; } = new();

        [JsonIgnore]
        private SolidColorBrush? _cachedBrush;

        [JsonIgnore]
        public SolidColorBrush ColorBrush
        {
            get
            {
                if (_cachedBrush != null) return _cachedBrush;
                try
                {
                    var hex = Color.StartsWith("#") ? Color[1..] : Color;
                    if (hex.Length == 6)
                    {
                        byte r = Convert.ToByte(hex[0..2], 16);
                        byte g = Convert.ToByte(hex[2..4], 16);
                        byte b = Convert.ToByte(hex[4..6], 16);
                        _cachedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
                        return _cachedBrush;
                    }
                }
                catch { }
                _cachedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250));
                return _cachedBrush;
            }
        }

        // Display name when no DataTemplate is present
        public override string ToString() => Name;
    }
}