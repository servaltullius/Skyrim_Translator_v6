using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class ParticleRules
{
    private static readonly Regex UnresolvedParticleMarkerRegex = new(
        pattern: @"을\(를\)|를\(을\)|은\(는\)|는\(은\)|을/를|를/을|은/는|는/은|\(\s*을\s*\)\s*를|\(\s*를\s*\)\s*을|\(\s*은\s*\)\s*는|\(\s*는\s*\)\s*은|이\(가\)|가\(이\)|이/가|가/이|\(\s*이\s*\)\s*가|\(\s*가\s*\)\s*이|과\(와\)|와\(과\)|과/와|와/과|\(\s*와\s*\)\s*과|\(\s*과\s*\)\s*와|으로\(로\)|로\(으로\)|으로/로|로/으로|\(\s*으\s*\)\s*로",
        options: RegexOptions.CultureInvariant
    );

    public static void Apply(LqaScanEntry entry, string sourceText, string destText, bool isKorean, List<LqaIssue> issues)
    {
        if (!isKorean)
        {
            return;
        }

        AddPrimaryParticleIssue(entry, sourceText, destText, issues);
        TryAddDuplicationArtifactIssue(entry, sourceText, destText, issues);
        TryAddPercentArtifactIssue(entry, sourceText, destText, issues);
    }

    private static void AddPrimaryParticleIssue(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        if (TryAddUnresolvedParticleMarkerIssue(entry, sourceText, destText, issues))
        {
            return;
        }

        if (TryAddDoubledParticleIssue(entry, sourceText, destText, issues))
        {
            return;
        }

        if (TryAddHangulParticleMismatchIssue(entry, sourceText, destText, issues))
        {
            return;
        }

        TryAddRomanParticleMismatchIssue(entry, sourceText, destText, issues);
    }

    private static bool TryAddUnresolvedParticleMarkerIssue(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        if (!UnresolvedParticleMarkerRegex.IsMatch(destText))
        {
            return false;
        }

        AddWarnIssue(entry, sourceText, destText, issues, "particle_marker", "조사 표기(괄호/슬래시 형태)가 그대로 남아있습니다.");
        return true;
    }

    private static bool TryAddDoubledParticleIssue(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var doubled = LqaHeuristics.FindDoubledParticleExample(destText);
        if (string.IsNullOrWhiteSpace(doubled))
        {
            return false;
        }

        AddWarnIssue(entry, sourceText, destText, issues, "particle_double", $"조사 병기/오타가 남아있습니다: '{doubled}'.");
        return true;
    }

    private static bool TryAddHangulParticleMismatchIssue(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var mismatch = LqaHeuristics.FindHangulParticleMismatchSuggestion(destText);
        if (string.IsNullOrWhiteSpace(mismatch))
        {
            return false;
        }

        AddWarnIssue(entry, sourceText, destText, issues, "particle_mismatch", $"조사 오류 가능성: {mismatch}");
        return true;
    }

    private static void TryAddRomanParticleMismatchIssue(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var romanMismatch = LqaHeuristics.FindRomanVowelParticleMismatchSuggestion(destText);
        if (string.IsNullOrWhiteSpace(romanMismatch))
        {
            return;
        }

        AddWarnIssue(entry, sourceText, destText, issues, "particle_roman_mismatch", $"조사 오류 가능성(로마자): {romanMismatch}");
    }

    private static void TryAddDuplicationArtifactIssue(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var duplication = LqaHeuristics.FindDuplicationArtifactExample(destText);
        if (string.IsNullOrWhiteSpace(duplication))
        {
            return;
        }

        AddWarnIssue(entry, sourceText, destText, issues, "dup_artifact", $"중복/오타 패턴이 감지되었습니다: '{duplication}'.");
    }

    private static void TryAddPercentArtifactIssue(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var percentArtifact = LqaHeuristics.FindPercentArtifactExample(destText);
        if (string.IsNullOrWhiteSpace(percentArtifact))
        {
            return;
        }

        AddWarnIssue(entry, sourceText, destText, issues, "percent_artifact", $"퍼센트 표기 오류 가능성: '{percentArtifact}'.");
    }

    private static void AddWarnIssue(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        List<LqaIssue> issues,
        string code,
        string message
    )
    {
        issues.Add(
            new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: code,
                Message: message,
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
