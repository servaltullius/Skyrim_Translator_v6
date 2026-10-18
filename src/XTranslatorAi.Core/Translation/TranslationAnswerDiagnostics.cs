using System;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Why the translation run would not save a model's answer for one item as it is: the token check and the
/// placeholder check a batch result goes through, in words. For the agent mode, whose model sees only its next
/// request and not why a line came back; it changes nothing the run does.
/// </summary>
public static class TranslationAnswerDiagnostics
{
    /// <param name="requestText">The item's text as the request showed it (hint markers and end sentinel included).</param>
    /// <param name="answer">The model's translation of that item.</param>
    /// <returns>The reason, or null when both checks pass.</returns>
    public static string? Explain(string requestText, string answer, string targetLang, PlaceholderSemanticRepairMode mode)
    {
        var masked = Clean(requestText);
        var output = Clean(answer);
        string candidate;
        try
        {
            candidate = TokenSanitizer.EnsureTokensPreservedOrRepair(masked, output, context: "agent", glossaryTokenToReplacement: null);
        }
        catch (InvalidOperationException ex)
        {
            return "보호 토큰·태그·줄바꿈이 원문과 맞지 않습니다: " + ex.Message.Trim();
        }

        return TokenSanitizer.DescribePlaceholderSemanticIssue(masked, candidate, targetLang, mode);
    }

    private static string Clean(string text)
        => GlossarySemanticHintInjector.Strip(PlaceholderSemanticHintInjector.Strip(text))
            .Replace(TranslationConstants.EndSentinelToken, "", StringComparison.Ordinal)
            .Trim();
}
