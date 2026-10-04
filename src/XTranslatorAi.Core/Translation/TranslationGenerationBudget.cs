using System;
using System.Collections.Generic;
using System.Linq;

namespace XTranslatorAi.Core.Translation;

/// <summary>A generation-call limit, shared across model runs and API-key failover. Not a dollar budget.</summary>
public sealed class TranslationGenerationBudget
{
    private readonly object _gate = new();
    private readonly int _maxRecoveryCallsPerRow;
    private readonly int _explicitTotalLimit;
    private readonly Dictionary<long, long> _baselineAllowances = new();
    private readonly Dictionary<long, int> _recoveryCalls = new();
    private readonly HashSet<long> _attemptedRows = new();
    private long _automaticLimit;
    private long _totalCalls;

    public TranslationGenerationBudget(int maxRecoveryCallsPerRow = 8, int maxTotalCalls = 0)
    {
        _maxRecoveryCallsPerRow = Math.Clamp(maxRecoveryCallsPerRow, 0, 100);
        _explicitTotalLimit = Math.Max(0, maxTotalCalls);
    }

    public long TotalCalls { get { lock (_gate) return _totalCalls; } }
    public long TotalLimit { get { lock (_gate) return GetTotalLimit(); } }

    /// <summary>The rows among <paramref name="ids"/> that an earlier request already carried.</summary>
    internal IReadOnlySet<long> GetAttempted(IEnumerable<long> ids)
    {
        lock (_gate) return ids.Where(_attemptedRows.Contains).ToHashSet();
    }

    /// <summary>The rows among <paramref name="ids"/> that have no recovery call left.</summary>
    internal IReadOnlySet<long> GetRowsOutOfRecoveryCalls(IEnumerable<long> ids)
    {
        lock (_gate) return ids.Where(id => _recoveryCalls.GetValueOrDefault(id) >= _maxRecoveryCallsPerRow).ToHashSet();
    }

    internal TranslationGenerationLimitException CreateRowLimitException(long rowId)
        => new(false, $"행 {rowId}의 추가 생성 호출 상한({_maxRecoveryCallsPerRow})에 도달했습니다.");

    public void RegisterRow(long rowId, int maskedChars, int protectedTokens)
    {
        // Generous ceiling for legitimate initial splitting (minimum retry chunk is
        // 256 chars). Recoveries have a separate, tighter per-original-row limit.
        var allowance = 1L + (Math.Max(0, maskedChars) + 127L) / 128
            + Math.Max(0, protectedTokens) + _maxRecoveryCallsPerRow;
        lock (_gate)
        {
            var old = _baselineAllowances.GetValueOrDefault(rowId);
            if (allowance > old)
            {
                _baselineAllowances[rowId] = allowance;
                _automaticLimit += allowance - old;
            }
        }
    }

    internal void Consume(IReadOnlyList<long> rowIds, bool recovery)
        => Consume(rowIds, recovery ? rowIds : Array.Empty<long>());

    /// <summary>
    /// Counts one call carrying <paramref name="rowIds"/>; it is a recovery call only for <paramref name="retriedRowIds"/>.
    /// After an API-key switch a batch mixes rows tried under the old key with rows never sent, and the new rows'
    /// first attempt must not use up their recovery allowance.
    /// </summary>
    internal void Consume(IReadOnlyList<long> rowIds, IReadOnlyCollection<long> retriedRowIds)
    {
        var ids = rowIds.Distinct().ToArray();
        var retried = retriedRowIds.Distinct().ToArray();
        lock (_gate)
        {
            if (_totalCalls >= GetTotalLimit())
                throw new TranslationGenerationLimitException(true, $"작업 생성 호출 상한({GetTotalLimit()})에 도달했습니다. 완료 결과는 보존됩니다.");
            foreach (var id in retried)
                if (_recoveryCalls.GetValueOrDefault(id) >= _maxRecoveryCallsPerRow)
                    throw CreateRowLimitException(id);

            _totalCalls++;
            foreach (var id in ids) _attemptedRows.Add(id);
            foreach (var id in retried)
            {
                _attemptedRows.Add(id);
                _recoveryCalls[id] = _recoveryCalls.GetValueOrDefault(id) + 1;
            }
        }
    }

    private long GetTotalLimit() => _explicitTotalLimit > 0 ? _explicitTotalLimit : Math.Max(1, _automaticLimit);
}

public sealed class TranslationGenerationLimitException(bool isRunLimit, string message) : Exception(message)
{
    public bool IsRunLimit { get; } = isRunLimit;
}
