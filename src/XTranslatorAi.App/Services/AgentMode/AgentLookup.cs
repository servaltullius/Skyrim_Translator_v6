using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>One entry that has the looked-up word: where it is, its English and its translation.</summary>
public sealed record AgentLookupHit(string Where, string Source, string Target);

/// <summary>
/// What the translator already calls an English word: the project and global glossaries, the game dictionary (the
/// whole text only, since it keeps a fingerprint of the English), the series TM, and the mod's own rows translated so
/// far. A name the request's glossary does not carry (Stalhrim, Ratway) is written from memory otherwise, and then
/// differs from the game's 스탈림, 쥐소굴. The mod's rows carry the names other lanes chose: in Meridia's Order three
/// lanes wrote Reeshi 리쉬 and 리시, and the Malevolent 악의단 and 말레볼런트, each unaware of the others.
/// </summary>
public static class AgentLookup
{
    public sealed record Result(IReadOnlyList<AgentLookupHit> Glossary, GameStringMatch? Game, IReadOnlyList<AgentLookupHit> Memory,
        int MemoryTotal, IReadOnlyList<AgentLookupHit>? Mod = null, int ModTotal = 0);

    public static async Task<Result> FindAsync(ProjectDb project, ProjectDb? global, GameStringDictionary? game, string word,
        int max, CancellationToken cancellationToken)
    {
        word = word.Trim();
        if (word.Length == 0) return new Result(Array.Empty<AgentLookupHit>(), null, Array.Empty<AgentLookupHit>(), 0);
        var pattern = new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var glossary = new List<AgentLookupHit>();
        foreach (var (where, db) in new[] { ("프로젝트 용어집", project), ("전역 용어집", global) })
        {
            if (db == null) continue;
            glossary.AddRange((await db.GetGlossaryAsync(cancellationToken))
                .Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.TargetTerm) && pattern.IsMatch(e.SourceTerm))
                .Select(e => new AgentLookupHit(where, e.SourceTerm, e.TargetTerm)));
        }

        var memory = global == null
            ? new List<AgentLookupHit>()
            : (await global.GetTranslationMemoryEntriesAsync("english", "korean", cancellationToken))
                .Where(e => !string.IsNullOrWhiteSpace(e.DestText) && pattern.IsMatch(e.SourceText))
                .Select(e => new AgentLookupHit("TM", e.SourceText, e.DestText))
                .ToList();
        // The mod's own rows translated so far (by this lane, another lane or the app), shortest first.
        var mod = (await project.GetTranslatedPairsAsync(null, cancellationToken))
            .Where(r => pattern.IsMatch(r.SourceText))
            .Select(r => new AgentLookupHit("이 모드", r.SourceText, r.DestText))
            .ToList();
        return new Result(Ranked(glossary, word).Take(max).ToList(), game?.Find(null, null, word), Ranked(memory, word).Take(max).ToList(),
            memory.Count, Ranked(mod, word).Take(Math.Min(max, 5)).ToList(), mod.Count);
    }

    // The word itself first, then the shortest entries: a name's own entry says how it is written, a long line only uses it.
    private static IEnumerable<AgentLookupHit> Ranked(IEnumerable<AgentLookupHit> hits, string word)
        => hits
            .DistinctBy(h => (h.Where, h.Source.Trim().ToLowerInvariant(), h.Target.Trim()))
            .OrderBy(h => string.Equals(h.Source.Trim(), word, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(h => h.Source.Length);
}
