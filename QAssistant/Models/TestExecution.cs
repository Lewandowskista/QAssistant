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
    }
}
