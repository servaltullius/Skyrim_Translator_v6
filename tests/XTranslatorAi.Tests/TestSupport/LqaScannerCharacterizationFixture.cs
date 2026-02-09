using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

internal static class LqaScannerCharacterizationFixture
{
    public static IReadOnlyList<LqaScanEntry> BuildRepresentativeEntries()
    {
        return new List<LqaScanEntry>
        {
            new(
                Id: 101,
                OrderIndex: 10,
                Edid: "INFO_TM_001",
                Rec: "INFO:000001",
                Status: StringEntryStatus.Done,
                SourceText: "Saarthal Amulet",
                DestText: "Saarthal Amulet"
            ),
            new(
                Id: 102,
                OrderIndex: 20,
                Edid: "MGEF_TOKEN_001",
                Rec: "MGEF:000002",
                Status: StringEntryStatus.Done,
                SourceText: "Absorb <mag> points [pagebreak] __XT_ONE__.",
                DestText: "매지카 을(를) 흡수합니다."
            ),
            new(
                Id: 103,
                OrderIndex: 30,
                Edid: "MESG_LONG_001",
                Rec: "MESG:000003",
                Status: StringEntryStatus.Edited,
                SourceText: "Quest objective updated",
                DestText: new string('가', 180) + "다"
            ),
            new(
                Id: 104,
                OrderIndex: 40,
                Edid: "MGEF_ROMAN_001",
                Rec: "MGEF:000004",
                Status: StringEntryStatus.Done,
                SourceText: "Meet Aela",
                DestText: "Aela을 (테스트"
            ),
            new(
                Id: 201,
                OrderIndex: 50,
                Edid: "NPC_GREETING_001",
                Rec: "DIAL:000101",
                Status: StringEntryStatus.Done,
                SourceText: "Hello.",
                DestText: "안내합니다."
            ),
            new(
                Id: 202,
                OrderIndex: 51,
                Edid: "NPC_GREETING_002",
                Rec: "DIAL:000102",
                Status: StringEntryStatus.Done,
                SourceText: "Hello.",
                DestText: "설명합니다."
            ),
            new(
                Id: 203,
                OrderIndex: 52,
                Edid: "NPC_GREETING_003",
                Rec: "INFO:000103",
                Status: StringEntryStatus.Done,
                SourceText: "Hello.",
                DestText: "정리합니다."
            ),
            new(
                Id: 204,
                OrderIndex: 53,
                Edid: "NPC_GREETING_004",
                Rec: "DIAL:000104",
                Status: StringEntryStatus.Done,
                SourceText: "Hello.",
                DestText: "이제 가요."
            ),
        };
    }

    public static IReadOnlyList<GlossaryEntry> BuildRepresentativeGlossary()
    {
        return new List<GlossaryEntry>
        {
            new(
                Id: 1,
                Category: "quests",
                SourceTerm: "points",
                TargetTerm: "포인트",
                Enabled: true,
                MatchMode: GlossaryMatchMode.WordBoundary,
                ForceMode: GlossaryForceMode.ForceToken,
                Priority: 10,
                Note: "Characterization fixture"
            ),
        };
    }

    public static IReadOnlyDictionary<long, string> BuildRepresentativeTmFallbackNotes()
    {
        return new Dictionary<long, string>
        {
            [101] = "  TM 폴백 메모  ",
        };
    }

    public static IReadOnlyList<LqaScanEntry> BuildPendingOnlyEntries()
    {
        return new List<LqaScanEntry>
        {
            new(
                Id: 999,
                OrderIndex: 1,
                Edid: "SKIP_ME",
                Rec: "MGEF:009999",
                Status: StringEntryStatus.Pending,
                SourceText: "Saarthal Amulet",
                DestText: "Saarthal Amulet"
            ),
        };
    }

    public static async Task<List<LqaIssue>> ScanRepresentativeAsync()
    {
        return await LqaScanner.ScanAsync(
            BuildRepresentativeEntries(),
            targetLang: "ko-KR",
            forceTokenGlossary: BuildRepresentativeGlossary(),
            tmFallbackNotes: BuildRepresentativeTmFallbackNotes()
        );
    }

    public static void AssertRepresentativeIssues(IReadOnlyList<LqaIssue> issues)
    {
        Assert.Collection(
            issues,
            issue =>
            {
                AssertIssueShape(issue, id: 102, orderIndex: 20, severity: "Error", code: "token_mismatch");
                AssertMessageContainsAll(issue.Message, "태그·토큰", "[pagebreak]", "__XT_*__");
            },
            issue =>
            {
                AssertIssueShape(issue, id: 101, orderIndex: 10, severity: "Warn", code: "tm_fallback");
                Assert.Equal("TM 폴백 메모", issue.Message);
            },
            issue =>
            {
                AssertIssueShape(issue, id: 101, orderIndex: 10, severity: "Warn", code: "untranslated");
                AssertMessageContainsAll(issue.Message, "원문과 동일", "미번역");
            },
            issue =>
            {
                AssertIssueShape(issue, id: 102, orderIndex: 20, severity: "Warn", code: "glossary_missing");
                AssertMessageContainsAll(issue.Message, "용어 누락", "points", "포인트");
            },
            issue =>
            {
                AssertIssueShape(issue, id: 102, orderIndex: 20, severity: "Warn", code: "particle_marker");
                AssertMessageContainsAll(issue.Message, "조사 표기", "괄호/슬래시");
            },
            issue =>
            {
                AssertIssueShape(issue, id: 103, orderIndex: 30, severity: "Warn", code: "length_risk");
                AssertLengthRiskMessage(issue.Message, expectedSourceLength: 23, expectedDestLength: 181, expectedRatio: 7.87);
            },
            issue =>
            {
                AssertIssueShape(issue, id: 103, orderIndex: 30, severity: "Warn", code: "rec_tone");
                AssertMessageContainsAll(issue.Message, "UI/퀘스트 톤", "합니다체", "PlainDa");
            },
            issue =>
            {
                AssertIssueShape(issue, id: 104, orderIndex: 40, severity: "Warn", code: "bracket_mismatch");
                AssertMessageContainsAll(issue.Message, "괄호/대괄호", "짝");
            },
            issue =>
            {
                AssertIssueShape(issue, id: 104, orderIndex: 40, severity: "Warn", code: "english_residue");
                AssertMessageContainsAll(issue.Message, "영문");
            },
            issue =>
            {
                AssertIssueShape(issue, id: 104, orderIndex: 40, severity: "Warn", code: "particle_roman_mismatch");
                AssertMessageContainsAll(issue.Message, "로마자", "Aela을", "Aela를");
            },
            issue =>
            {
                AssertIssueShape(issue, id: 204, orderIndex: 53, severity: "Warn", code: "tone_inconsistent");
                AssertMessageContainsAll(issue.Message, "대사 그룹", "말투", "majority=Hamnida");
            }
        );
    }

    private static void AssertIssueShape(LqaIssue issue, long id, int orderIndex, string severity, string code)
    {
        Assert.Equal(id, issue.Id);
        Assert.Equal(orderIndex, issue.OrderIndex);
        Assert.Equal(severity, issue.Severity);
        Assert.Equal(code, issue.Code);
    }

    private static void AssertMessageContainsAll(string message, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            Assert.Contains(fragment, message);
        }
    }

    private static void AssertLengthRiskMessage(
        string message,
        int expectedSourceLength,
        int expectedDestLength,
        double expectedRatio
    )
    {
        AssertMessageContainsAll(message, "길이 위험", $"src={expectedSourceLength}", $"dst={expectedDestLength}");

        var ratioMarker = ", x";
        var markerIndex = message.LastIndexOf(ratioMarker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Expected '{ratioMarker}' marker in length_risk message: '{message}'.");

        var ratioText = message[(markerIndex + ratioMarker.Length)..].Trim();
        Assert.True(
            TryParseLocaleSafeDouble(ratioText, out var ratio),
            $"Could not parse length_risk ratio from '{message}'. ratioText='{ratioText}'."
        );
        Assert.InRange(ratio, expectedRatio - 0.01, expectedRatio + 0.01);
    }

    private static bool TryParseLocaleSafeDouble(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        var normalized = text.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
