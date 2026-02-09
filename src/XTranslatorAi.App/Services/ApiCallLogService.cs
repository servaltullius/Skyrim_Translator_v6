using System.Globalization;
using XTranslatorAi.App.Collections;
using XTranslatorAi.App.ViewModels;

namespace XTranslatorAi.App.Services;

public sealed class ApiCallLogService
{
    private readonly int _maxEntries;

    public ObservableRangeCollection<ApiCallLogRow> Rows { get; } = new();

    public ApiCallLogService(int maxEntries = 2000)
    {
        _maxEntries = maxEntries;
    }

    public void Clear() => Rows.Clear();

    public void Add(ApiCallLogRow row)
    {
        Rows.Add(row);
        Trim();
    }

    public string TotalsSummary
    {
        get
        {
            var totals = ComputeTotals();
            return $"Σ InTok {N(totals.InTok)} · OutTok {N(totals.OutTok)} · Cost ${totals.CostUsd:0.####}";
        }
    }

    public string TotalsToolTip
    {
        get
        {
            var totals = ComputeTotals();
            return
                $"Calls: {N(totals.Calls)} (OK {N(totals.OkCalls)} / Fail {N(totals.FailCalls)})\n"
                + $"Σ InTok: {N(totals.InTok)} (rows {N(totals.InTokRows)})\n"
                + $"Σ OutTok: {N(totals.OutTok)} (rows {N(totals.OutTokRows)})\n"
                + $"Σ Cost: ${totals.CostUsd:0.####} (rows {N(totals.CostRows)})";
        }
    }

    private readonly record struct Totals(
        int Calls,
        int OkCalls,
        int FailCalls,
        long InTok,
        long OutTok,
        int InTokRows,
        int OutTokRows,
        double CostUsd,
        int CostRows
    );

    private Totals ComputeTotals()
    {
        var calls = Rows.Count;
        var okCalls = 0;
        var failCalls = 0;
        long inTok = 0;
        long outTok = 0;
        var inTokRows = 0;
        var outTokRows = 0;
        var costUsd = 0.0;
        var costRows = 0;

        foreach (var row in Rows)
        {
            if (row.Success)
            {
                okCalls++;
            }
            else
            {
                failCalls++;
            }

            if (row.PromptTokens is { } p and >= 0)
            {
                inTok += p;
                inTokRows++;
            }

            if (row.CompletionTokens is { } c and >= 0)
            {
                outTok += c;
                outTokRows++;
            }

            if (row.CostUsd is { } cost and >= 0)
            {
                costUsd += cost;
                costRows++;
            }
        }

        return new Totals(
            Calls: calls,
            OkCalls: okCalls,
            FailCalls: failCalls,
            InTok: inTok,
            OutTok: outTok,
            InTokRows: inTokRows,
            OutTokRows: outTokRows,
            CostUsd: costUsd,
            CostRows: costRows
        );
    }

    private void Trim()
    {
        while (Rows.Count > _maxEntries)
        {
            Rows.RemoveAt(0);
        }
    }

    private static string N(long v) => v.ToString("N0", CultureInfo.CurrentCulture);
}

