using System;

namespace QAssistant.Models
{
    public enum TestCaseStatus { NotRun, Passed, Failed, Blocked, Skipped }

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
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public string SourceIssueId { get; set; } = string.Empty;
        public TaskSource Source { get; set; } = TaskSource.Manual;
        public Guid? TestPlanId { get; set; }
    }
}
