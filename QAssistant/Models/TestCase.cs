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
    public enum TestCaseStatus { NotRun, Passed, Failed, Blocked, Skipped }
    public enum TestCasePriority { Low, Medium, Major, Blocker }

    public class TestCase
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string TestCaseId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string PreConditions { get; set; } = string.Empty;
        public string TestSteps { get; set; } = string.Empty;
        public string TestData { get; set; } = string.Empty;
        public string ExpectedResult { get; set; } = string.Empty;
        public string ActualResult { get; set; } = string.Empty;
        public TestCaseStatus Status { get; set; } = TestCaseStatus.NotRun;
        public TestCasePriority Priority { get; set; } = TestCasePriority.Medium;
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public string SourceIssueId { get; set; } = string.Empty;
        public TaskSource Source { get; set; } = TaskSource.Manual;
        public Guid? TestPlanId { get; set; }
    }
}
