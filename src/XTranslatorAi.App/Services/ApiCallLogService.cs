using System.Globalization;
using System.Linq;
using XTranslatorAi.App.Collections;
using XTranslatorAi.App.ViewModels;

namespace XTranslatorAi.App.Services;

public sealed class ApiCallLogService
{
    private readonly int _maxEntries;
    private ApiUsageTotals _totals;

    public ObservableRangeCollection<ApiCallLogRow> Rows { get; } = new();

    public ApiCallLogService(int maxEntries = 2000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _maxEntries = maxEntries;
    }

    public ApiUsageTotals UsageTotals => _totals;

    public void Clear()
    {
        Rows.Clear();
        _totals = default;
    }

    public void Add(ApiCallLogRow row)
    {
        var hasInput = row.PromptTokens is >= 0;
        var hasOutput = row.CompletionTokens is >= 0;
        var hasCost = row.CostUsd is { } cost && cost >= 0 && double.IsFinite(cost);
        _totals = new ApiUsageTotals(
            Calls: _totals.Calls + 1,
            OkCalls: _totals.OkCalls + (row.Success ? 1 : 0),
            FailCalls: _totals.FailCalls + (row.Success ? 0 : 1),
            InTok: _totals.InTok + (hasInput ? row.PromptTokens!.Value : 0),
            OutTok: _totals.OutTok + (hasOutput ? row.CompletionTokens!.Value : 0),
            InTokRows: _totals.InTokRows + (hasInput ? 1 : 0),
            OutTokRows: _totals.OutTokRows + (hasOutput ? 1 : 0),
            CostUsd: _totals.CostUsd + (hasCost ? row.CostUsd!.Value : 0),
            CostRows: _totals.CostRows + (hasCost ? 1 : 0)
        );
        Rows.Add(row);
        Trim();
    }

    public string TotalsSummary
    {
        get
        {
            var totals = _totals;
            var cost = totals.Calls > 0 && totals.CostRows == 0
                ? "미확인"
                : $"${totals.CostUsd:0.####}" + (totals.CostRows < totals.Calls ? " (일부 미확인)" : "");
            return $"Σ InTok {N(totals.InTok)} · OutTok {N(totals.OutTok)} · Cost {cost}";
        }
    }

    public string TotalsToolTip
    {
        get
        {
            var totals = _totals;
            return
                $"Calls: {N(totals.Calls)} (OK {N(totals.OkCalls)} / Fail {N(totals.FailCalls)})\n"
                + $"Σ InTok: {N(totals.InTok)} (rows {N(totals.InTokRows)})\n"
                + $"Σ OutTok: {N(totals.OutTok)} (rows {N(totals.OutTokRows)})\n"
                + $"Σ 확인된 Cost: ${totals.CostUsd:0.####} (rows {N(totals.CostRows)}; 비용 정보 없는 호출 {N(totals.Calls - totals.CostRows)}건)\n"
                + $"누계: 로그를 마지막으로 지운 이후 전체 호출. 화면에는 최근 {N(Rows.Count)}건만 표시합니다.";
        }
    }

    public readonly record struct ApiUsageTotals(
        long Calls,
        long OkCalls,
        long FailCalls,
        long InTok,
        long OutTok,
        long InTokRows,
        long OutTokRows,
        double CostUsd,
        long CostRows
    );

    private void Trim()
    {
        var excess = Rows.Count - _maxEntries;
        if (excess <= 0) return;

        var kept = Rows.Skip(excess).ToList();
        Rows.ReplaceAll(kept);
    }

    private static string N(long v) => v.ToString("N0", CultureInfo.CurrentCulture);
}
