using System;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// The run stopped because batches kept failing the same way: on a rate limit (typically a spent daily quota)
/// with API-key failover off, or on a lost connection or timeout (<see cref="IsConnectionFailure"/>), which no
/// key change fixes. Rows not yet translated are back to Pending; the inner exception is the last failure.
/// </summary>
public sealed class TranslationRateLimitAbortException(int consecutiveFailures, Exception inner, bool isConnectionFailure = false)
    : Exception(
        isConnectionFailure
            ? $"연결 문제로 {consecutiveFailures}번 연속 실패해 번역을 멈췄습니다. 남은 행은 대기 상태로 되돌렸습니다."
            : $"요청 제한으로 {consecutiveFailures}번 연속 실패해 번역을 멈췄습니다. 남은 행은 대기 상태로 되돌렸습니다.",
        inner)
{
    public int ConsecutiveFailures { get; } = consecutiveFailures;

    public bool IsConnectionFailure { get; } = isConnectionFailure;
}
