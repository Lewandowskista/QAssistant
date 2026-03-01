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
    public enum RunbookCategory { Deployment, GoLive, Hotfix, Rollback, Maintenance, Custom }
    public enum RunbookStepStatus { Pending, InProgress, Done, Skipped, Blocked }

    public class RunbookStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string AssignedTo { get; set; } = string.Empty;
        public RunbookStepStatus Status { get; set; } = RunbookStepStatus.Pending;
    }

    public class Runbook
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = "Untitled Runbook";
        public string Description { get; set; } = string.Empty;
        public RunbookCategory Category { get; set; } = RunbookCategory.Deployment;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public List<RunbookStep> Steps { get; set; } = new();
    }
}
