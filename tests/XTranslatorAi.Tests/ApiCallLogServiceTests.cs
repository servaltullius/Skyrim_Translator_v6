using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;

namespace XTranslatorAi.Tests;

public class ApiCallLogServiceTests
{
    [Fact]
    public void TrimmingVisibleRows_PreservesAllUsageTotals()
    {
        var service = new ApiCallLogService(maxEntries: 2);
        service.Add(Row(success: true, input: 100, output: 10, cost: 0.1));
        service.Add(Row(success: false, input: 200, output: null, cost: null));
        service.Add(Row(success: true, input: 300, output: 30, cost: 0.3));

        Assert.Equal(2, service.Rows.Count);
        Assert.Equal(3, service.UsageTotals.Calls);
        Assert.Equal(2, service.UsageTotals.OkCalls);
        Assert.Equal(1, service.UsageTotals.FailCalls);
        Assert.Equal(600, service.UsageTotals.InTok);
        Assert.Equal(40, service.UsageTotals.OutTok);
        Assert.Equal(0.4, service.UsageTotals.CostUsd, 8);
        Assert.Equal(2, service.UsageTotals.CostRows);
    }

    [Fact]
    public void Clear_ResetsBothHistoryAndUsage()
    {
        var service = new ApiCallLogService(1);
        service.Add(Row(true, 100, 50, 1));
        service.Add(Row(true, 100, 50, 1));
        service.Clear();

        Assert.Empty(service.Rows);
        Assert.Equal(default, service.UsageTotals);
        service.Add(Row(true, 7, 3, 0.01));
        Assert.Equal(1, service.UsageTotals.Calls);
        Assert.Equal(7, service.UsageTotals.InTok);
    }

    [Fact]
    public void MissingOrInvalidUsage_IsNotCountedAsKnownZeroCost()
    {
        var service = new ApiCallLogService();
        service.Add(Row(false, -1, null, double.NaN));
        Assert.Contains("Cost 미확인", service.TotalsSummary);
        service.Add(Row(true, 0, 0, 0));

        Assert.Equal(2, service.UsageTotals.Calls);
        Assert.Equal(1, service.UsageTotals.InTokRows);
        Assert.Equal(1, service.UsageTotals.OutTokRows);
        Assert.Equal(1, service.UsageTotals.CostRows);
        Assert.Equal(0, service.UsageTotals.CostUsd);
        Assert.Contains("일부 미확인", service.TotalsSummary);
    }

    [Fact]
    public void RejectedGeneration_WithUsage_ContributesToCostTotals()
    {
        var service = new ApiCallLogService();
        service.Add(Row(false, 100, 34, 0.25));
        Assert.Equal(1, service.UsageTotals.FailCalls);
        Assert.Equal(100, service.UsageTotals.InTok);
        Assert.Equal(34, service.UsageTotals.OutTok);
        Assert.Equal(0.25, service.UsageTotals.CostUsd);
        Assert.Equal(1, service.UsageTotals.CostRows);
    }

    private static ApiCallLogRow Row(bool success, int? input, int? output, double? cost)
        => new(DateTimeOffset.UtcNow, TimeSpan.Zero, "Test", "Translate", "test-model", null,
            success ? 200 : 429, success, null, input, output, null, cost);
}
