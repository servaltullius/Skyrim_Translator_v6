using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// A dialogue topic named like "SDA_DA09IntroTopic00" is a hidden topic the player never sees; its text must stay
/// as it is. Serana's were translated ("SDA_OP반응2", or a whole invented line) and the review put all of them back.
/// Effect and spell names in that form are translated on purpose, so only DIAL:FULL is checked.
/// </summary>
internal static class HiddenTopicRule
{
    // Letters, digits and underscores only: "LoNier." is a name written in two capitals, not an identifier.
    private static readonly Regex IdentifierRegex = new(@"^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);

    public static void Apply(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        if (!string.Equals((entry.Rec ?? "").Trim(), "DIAL:FULL", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(destText)
            || !IdentifierRegex.IsMatch(sourceText.Trim())
            || !LqaHeuristics.IsInternalIdentifier(sourceText.Trim())
            || string.Equals(sourceText.Trim(), destText.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Warn", "hidden_topic_translated",
            "숨은 대화 토픽 이름(식별자)을 번역했습니다. 원문 그대로 두세요.", sourceText, destText));
    }
}
