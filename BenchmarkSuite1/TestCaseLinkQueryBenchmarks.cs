using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using QAssistant.Models;
using Microsoft.VSDiagnostics;

[CPUUsageDiagnoser]
public class StatusCountBenchmarks
{
    private List<TestCase> _cases = null!;
    [Params(50, 500)]
    public int CaseCount;
    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        TestCaseStatus[] statuses = [TestCaseStatus.NotRun, TestCaseStatus.Passed, TestCaseStatus.Failed, TestCaseStatus.Blocked, TestCaseStatus.Skipped];
        _cases = Enumerable.Range(0, CaseCount).Select(_ => new TestCase { Status = statuses[rng.Next(statuses.Length)] }).ToList();
    }

    /// <summary>Current: 5 separate LINQ Count() passes over the list.</summary>
    [Benchmark(Baseline = true)]
    public (int, int, int, int, int) FiveLinqPasses()
    {
        var cases = _cases;
        int passed = cases.Count(c => c.Status == TestCaseStatus.Passed);
        int failed = cases.Count(c => c.Status == TestCaseStatus.Failed);
        int blocked = cases.Count(c => c.Status == TestCaseStatus.Blocked);
        int skipped = cases.Count(c => c.Status == TestCaseStatus.Skipped);
        int notRun = cases.Count(c => c.Status == TestCaseStatus.NotRun);
        return (passed, failed, blocked, skipped, notRun);
    }

    /// <summary>Optimised: single loop, increment five counters via switch.</summary>
    [Benchmark]
    public (int, int, int, int, int) SingleCountingLoop()
    {
        int passed = 0, failed = 0, blocked = 0, skipped = 0, notRun = 0;
        foreach (var c in _cases)
        {
            switch (c.Status)
            {
                case TestCaseStatus.Passed:
                    passed++;
                    break;
                case TestCaseStatus.Failed:
                    failed++;
                    break;
                case TestCaseStatus.Blocked:
                    blocked++;
                    break;
                case TestCaseStatus.Skipped:
                    skipped++;
                    break;
                default:
                    notRun++;
                    break;
            }
        }

        return (passed, failed, blocked, skipped, notRun);
    }
}

[CPUUsageDiagnoser]
public class PlanGroupingBenchmarks
{
    private List<TestCase> _cases = null!;
    private List<TestPlan> _plans = null!;
    [Params(5, 20)]
    public int PlanCount;
    [Params(100, 500)]
    public int CaseCount;
    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        TestCaseStatus[] statuses = [TestCaseStatus.NotRun, TestCaseStatus.Passed, TestCaseStatus.Failed, TestCaseStatus.Blocked, TestCaseStatus.Skipped];
        _plans = Enumerable.Range(0, PlanCount).Select(_ => new TestPlan { Id = Guid.NewGuid() }).ToList();
        Guid[] planIds = [.._plans.Select(p => p.Id)];
        _cases = Enumerable.Range(0, CaseCount).Select(_ => new TestCase { TestPlanId = planIds[rng.Next(planIds.Length)], Status = statuses[rng.Next(statuses.Length)] }).ToList();
    }

    /// <summary>
    /// Current pattern (table loop): for each plan, Where() + ToList() + Count(Passed).
    /// O(plans × cases) — allocates a new List per plan.
    /// </summary>
    [Benchmark(Baseline = true)]
    public List<(int total, int passed)> PerPlanLinearScan()
    {
        var result = new List<(int, int)>(_plans.Count);
        foreach (var plan in _plans)
        {
            var planCases = _cases.Where(tc => tc.TestPlanId == plan.Id).ToList();
            int planPassed = planCases.Count(c => c.Status == TestCaseStatus.Passed);
            result.Add((planCases.Count, planPassed));
        }

        return result;
    }

    /// <summary>
    /// Current pattern (filter flyout): for each plan, Count(tc => tc.TestPlanId == plan.Id).
    /// O(plans × cases).
    /// </summary>
    [Benchmark]
    public List<int> PerPlanCountScan()
    {
        var result = new List<int>(_plans.Count);
        foreach (var plan in _plans)
            result.Add(_cases.Count(tc => tc.TestPlanId == plan.Id));
        return result;
    }

    /// <summary>
    /// Optimised: one forward pass over cases builds a Dictionary, then O(1) lookup per plan.
    /// O(cases + plans) — no per-plan allocation.
    /// </summary>
    [Benchmark]
    public List<(int total, int passed)> PreGroupedLookup()
    {
        // Single pass: accumulate (total, passed) counts keyed by plan ID
        var byPlan = new Dictionary<Guid, (int total, int passed)>(_plans.Count);
        foreach (var tc in _cases)
        {
            if (!tc.TestPlanId.HasValue)
                continue;
            var id = tc.TestPlanId.Value;
            byPlan.TryGetValue(id, out var counts);
            counts.total++;
            if (tc.Status == TestCaseStatus.Passed)
                counts.passed++;
            byPlan[id] = counts;
        }

        var result = new List<(int, int)>(_plans.Count);
        foreach (var plan in _plans)
        {
            var v = byPlan.TryGetValue(plan.Id, out var counts) ? counts : default;
            result.Add((v.total, v.passed));
        }

        return result;
    }
}