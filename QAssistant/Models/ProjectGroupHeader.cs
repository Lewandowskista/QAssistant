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

namespace QAssistant.Models
{
    /// <summary>
    /// A non-selectable visual separator rendered in the Projects sidebar
    /// to group projects under a named client.
    /// </summary>
    public sealed class ProjectGroupHeader
    {
        public string ClientName { get; }
        public bool IsUngrouped => string.IsNullOrEmpty(ClientName);
        public string DisplayName => IsUngrouped ? "NO CLIENT" : ClientName.ToUpperInvariant();

        public ProjectGroupHeader(string clientName) => ClientName = clientName;
    }
}
