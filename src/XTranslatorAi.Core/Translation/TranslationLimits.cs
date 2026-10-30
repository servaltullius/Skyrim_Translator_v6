using System;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// The limits a run's batches and requests are held to, the same for the saved settings, the GUI's run, its cost estimate
/// and comparison, and agent mode: each clamped them with its own copy of the numbers.
/// </summary>
public static class TranslationLimits
{
    public static int BatchSize(int value) => Math.Clamp(value, 1, 100);

    public static int MaxChars(int value) => Math.Clamp(value, 1000, 50000);

    public static int Parallel(int value) => Math.Clamp(value, 1, 8);

    /// <summary>The longest row a batch of short rows takes; a longer one goes in the long rows' batches.</summary>
    public static int ShortRowMaxChars(int maxChars) => Math.Clamp(maxChars / 3, 1500, 6000);

    /// <summary>The batch size for long rows. The cost estimate mirrors the run's queues with its own copy of both.</summary>
    public static int LongBatchSize(int batchSize) => Math.Max(1, Math.Min(batchSize, 8));
}
