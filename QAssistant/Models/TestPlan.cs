using System;

namespace QAssistant.Models
{
    public class TestPlan
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string TestPlanId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public TaskSource Source { get; set; } = TaskSource.Manual;
    }
}
