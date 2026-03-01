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

namespace QAssistant.Models
{
    public enum ChecklistItemPriority { Low, Normal, High, Blocker }

    public class ChecklistItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Text { get; set; } = string.Empty;
        public bool IsChecked { get; set; }
        public string Notes { get; set; } = string.Empty;
        public ChecklistItemPriority Priority { get; set; } = ChecklistItemPriority.Normal;
    }

    public class ChecklistTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// Category such as "Pre-Deployment", "SAP Commerce", "Release Sign-off"
        public string Category { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<ChecklistItem> Items { get; set; } = new();
    }
}
