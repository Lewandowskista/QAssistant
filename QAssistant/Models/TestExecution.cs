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
    public class TestExecution
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ExecutionId { get; set; } = string.Empty;
        public Guid TestCaseId { get; set; }
        public Guid TestPlanId { get; set; }
        public TestCaseStatus Result { get; set; } = TestCaseStatus.NotRun;
        public string ActualResult { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; } = DateTime.Now;
        public bool IsArchived { get; set; } = false;
    }
}
