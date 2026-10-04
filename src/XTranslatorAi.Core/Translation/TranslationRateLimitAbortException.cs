using System;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// The run stopped because batches kept ending on a rate limit (typically a spent daily quota) with API-key
/// failover off. Rows not yet translated are back to Pending; the inner exception is the last rate-limit error.
/// </summary>
public sealed class TranslationRateLimitAbortException(int consecutiveFailures, Exception inner)
    : Exception($"요청 제한으로 {consecutiveFailures}번 연속 실패해 번역을 멈췄습니다. 남은 행은 대기 상태로 되돌렸습니다.", inner)
{
    public int ConsecutiveFailures { get; } = consecutiveFailures;
}
