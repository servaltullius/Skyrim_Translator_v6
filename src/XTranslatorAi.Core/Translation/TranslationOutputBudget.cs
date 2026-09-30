namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Conservative, opt-in output sizing for translation requests. These character-based
/// estimates are not token counts and do not predict billing or guarantee completion.
/// </summary>
public static class TranslationOutputBudget
{
    private const int ReasoningAllowance = 2048;
    private const int MinimumOutputTokens = 4096;
    private const int OutputIncrement = 256;

    public static int Compute(
        int sourceChars,
        int protectedTokenCount,
        int rowCount,
        int configuredCeiling,
        bool enabled)
    {
        if (!enabled)
        {
            return configuredCeiling;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuredCeiling);

        // Use long before multiplying: a malformed or very large input must not
        // wrap around into a smaller request budget.
        var estimate = 2L * Math.Max(0, sourceChars)
            + 16L * Math.Max(0, protectedTokenCount)
            + 64L * Math.Max(0, rowCount)
            + ReasoningAllowance;
        estimate = Math.Max(MinimumOutputTokens, estimate);
        var rounded = ((estimate + OutputIncrement - 1) / OutputIncrement) * OutputIncrement;

        // The manual ceiling wins even if it is below the floor or not a multiple
        // of the rounding increment.
        return (int)Math.Min(configuredCeiling, rounded);
    }

    public static int GetSourceCharLimit(
        int configuredMaxChars,
        int configuredOutputCeiling,
        bool enabled)
    {
        if (!enabled)
        {
            return configuredMaxChars;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuredOutputCeiling);

        // This is only a coarse splitting threshold. Compute adds row and protected
        // token allowances to each actual request; neither heuristic is a tokenizer.
        var outputDerivedLimit = Math.Max(256L, ((long)configuredOutputCeiling - ReasoningAllowance) / 2);
        return (int)Math.Min(Math.Max(0, configuredMaxChars), outputDerivedLimit);
    }
}
