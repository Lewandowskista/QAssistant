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
    public class ApiRequestHistoryEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime ExecutedAt { get; set; } = DateTime.Now;
        public int StatusCode { get; set; }
        public string ResponseBody { get; set; } = string.Empty;
        public string ResponseHeaders { get; set; } = string.Empty;
        public long DurationMs { get; set; }
    }

    public class SavedApiRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Method { get; set; } = "GET";
        public string Url { get; set; } = string.Empty;

        /// JSON string of header key-value pairs
        public string Headers { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;

        /// Category such as "OCC", "HAC", "Jira", "Linear", "Custom"
        public string Category { get; set; } = "Custom";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<ApiRequestHistoryEntry> History { get; set; } = new();
    }
}
