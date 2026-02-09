using System;
using System.Collections.Generic;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static void EnsureChunkTokensPreserved(string inputChunk, string outputChunk)
    {
        ValidateTokensPreserved(inputChunk, outputChunk, context: "chunk");
    }

    private static void ValidateTokenIntegrity(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch,
        IReadOnlyDictionary<long, string> translations
    )
    {
        foreach (var it in batch)
        {
            if (!translations.TryGetValue(it.Id, out var output))
            {
                throw new InvalidOperationException($"Model output missing id: {it.Id}");
            }

            ValidateTokensPreserved(it.Masked, output, context: $"id={it.Id}");
        }
    }

    private static string EnsureTokensPreservedOrRepair(
        string inputText,
        string outputText,
        string context,
        IReadOnlyDictionary<string, string>? glossaryTokenToReplacement = null
    )
    {
        outputText = SanitizeModelTranslationText(outputText, inputText);

        var cleanedOutput = RemoveBrokenXtTokenMarkers(outputText);
        if (!ReferenceEquals(cleanedOutput, outputText) && !string.Equals(cleanedOutput, outputText, StringComparison.Ordinal))
        {
            outputText = cleanedOutput;
        }

        outputText = RepairMagDurSemanticMixups(outputText, inputText);
        outputText = RepairDurTokenMisplacedAfterKoreanTimePhrase(outputText, inputText);
        outputText = RepairKoreanBadParticlesOnNumericPlaceholders(outputText, inputText);

        try
        {
            ValidateTokensPreserved(inputText, outputText, context);
            ValidateNotTruncatedOrOmitted(inputText, outputText, context);
            ValidateRawTagsPreserved(inputText, outputText, context);
            return outputText;
        }
        catch (InvalidOperationException)
        {
            if (TryRepairTokens(inputText, outputText, glossaryTokenToReplacement, out var repaired))
            {
                ValidateTokensPreserved(inputText, repaired, context);
                ValidateNotTruncatedOrOmitted(inputText, repaired, context);
                ValidateRawTagsPreserved(inputText, repaired, context);
                return repaired;
            }

            throw;
        }
    }

    private static string EnsureTokensPreservedOrThrow(
        string inputText,
        string outputText,
        string context
    )
    {
        outputText = SanitizeModelTranslationText(outputText, inputText);

        var cleanedOutput = RemoveBrokenXtTokenMarkers(outputText);
        if (!ReferenceEquals(cleanedOutput, outputText) && !string.Equals(cleanedOutput, outputText, StringComparison.Ordinal))
        {
            outputText = cleanedOutput;
        }

        outputText = RepairMagDurSemanticMixups(outputText, inputText);
        outputText = RepairDurTokenMisplacedAfterKoreanTimePhrase(outputText, inputText);
        outputText = RepairKoreanBadParticlesOnNumericPlaceholders(outputText, inputText);

        ValidateTokensPreserved(inputText, outputText, context);
        ValidateNotTruncatedOrOmitted(inputText, outputText, context);
        ValidateRawTagsPreserved(inputText, outputText, context);
        return outputText;
    }
}
