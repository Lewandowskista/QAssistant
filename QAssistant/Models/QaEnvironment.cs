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
    public enum EnvironmentType { Development, Staging, Production, Custom }

    public class QaEnvironment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public EnvironmentType Type { get; set; } = EnvironmentType.Custom;
        public string Color { get; set; } = "#A78BFA";
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Generic web endpoints
        public string BaseUrl { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;

        // Health-check endpoint (falls back to BaseUrl if empty)
        public string HealthCheckUrl { get; set; } = string.Empty;

        // SAP Commerce–specific endpoints
        public string HacUrl { get; set; } = string.Empty;
        public string BackofficeUrl { get; set; } = string.Empty;
        public string StorefrontUrl { get; set; } = string.Empty;
        public string SolrAdminUrl { get; set; } = string.Empty;
        public string OccBasePath { get; set; } = string.Empty;

        // SSL: bypass certificate validation for self-signed / internal certs (development only — never use in production)
        public bool IgnoreSslErrors { get; set; } = false;

        // Auth stored separately in CredentialService using key "Env_{Id}_Username" etc.
    }
}
