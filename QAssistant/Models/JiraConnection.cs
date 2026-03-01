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

namespace QAssistant.Models
{
    /// <summary>
    /// Stores the non-sensitive configuration for a single Jira connection.
    /// The API token is kept in the Windows Credential Store under
    /// "{projectId}_JiraApiToken_{Id}".
    /// </summary>
    public class JiraConnection
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Label { get; set; } = "Default";
        public string Domain { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ProjectKey { get; set; } = string.Empty;
    }
}
