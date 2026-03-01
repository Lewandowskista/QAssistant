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
    public class TestDataGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;

        /// Category such as "Users", "Products", "Promotions", "Cart", "Orders"
        public string Category { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<TestDataEntry> Entries { get; set; } = new();
    }

    public class TestDataEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// Comma-separated tags, e.g. "b2b,staging"
        public string Tags { get; set; } = string.Empty;

        /// "All", or an environment name / type
        public string Environment { get; set; } = "All";
    }
}
