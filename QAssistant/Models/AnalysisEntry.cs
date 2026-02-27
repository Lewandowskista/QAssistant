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
