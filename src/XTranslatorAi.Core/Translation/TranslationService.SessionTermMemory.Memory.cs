using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    internal sealed class SessionTermMemory
    {
        private sealed record SessionTermEntry(string Target, string Token, bool AllowForce, bool Conflicted = false);

        private readonly ConcurrentDictionary<string, SessionTermEntry> _termToEntry;
        private readonly int _maxTerms;
        private readonly object _learnLock = new();
        private int _nextTokenId = -1;

        public SessionTermMemory(int maxTerms)
        {
            _maxTerms = Math.Max(0, maxTerms);
            _termToEntry = new ConcurrentDictionary<string, SessionTermEntry>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsEmpty => _termToEntry.IsEmpty;

        public bool Knows(string term) => _termToEntry.ContainsKey(NormalizeSessionTermKey(term));

        public bool TryLearn(string sourceTerm, string targetTranslation, bool allowForce = true)
        {
            if (_maxTerms <= 0)
            {
                return false;
            }

            var key = NormalizeSessionTermKey(sourceTerm);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var target = (targetTranslation ?? "").Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (!allowForce && !IsAutomaticSessionTermCandidate(sourceTerm)) return false;

            lock (_learnLock)
            {
                if (_termToEntry.TryGetValue(key, out var existing))
                {
                    // Explicit preloaded terms are authoritative. Automatic observations
                    // never overwrite them; competing automatic translations stop reuse.
                    if (existing.AllowForce) return false;
                    if (allowForce)
                    {
                        _termToEntry[key] = new SessionTermEntry(target, existing.Token, true);
                        return true;
                    }
                    if (!string.Equals(existing.Target, target, StringComparison.Ordinal))
                        _termToEntry[key] = existing with { Conflicted = true };
                    return false;
                }
                if (_termToEntry.Count >= _maxTerms) return false;
                var tokenId = Interlocked.Increment(ref _nextTokenId);
                var token = $"__XT_TERM_SESS_{tokenId:0000}__";
                return _termToEntry.TryAdd(key, new SessionTermEntry(target, token, allowForce));
            }
        }

        public bool IsConflicted(string source)
            => _termToEntry.TryGetValue(NormalizeSessionTermKey(source), out var entry) && entry.Conflicted;

        public IReadOnlyList<(string Source, string Target)> MergeForText(
            string text,
            IReadOnlyList<(string Source, string Target)> basePromptOnlyGlossary
        )
        {
            if (_termToEntry.IsEmpty)
            {
                return basePromptOnlyGlossary;
            }

            var excludedSources = BuildExcludedSources(basePromptOnlyGlossary);
            var sessionPairs = GetRelevantPairsForTexts(new[] { text }, excludedSources);
            if (sessionPairs.Count == 0)
            {
                return basePromptOnlyGlossary;
            }

            return MergeLists(basePromptOnlyGlossary, sessionPairs);
        }

        public IReadOnlyList<(string Source, string Target)> MergeForTexts(
            IReadOnlyList<string> texts,
            IReadOnlyList<(string Source, string Target)> basePromptOnlyGlossary
        )
        {
            if (_termToEntry.IsEmpty || texts.Count == 0)
            {
                return basePromptOnlyGlossary;
            }

            var excludedSources = BuildExcludedSources(basePromptOnlyGlossary);
            var sessionPairs = GetRelevantPairsForTexts(texts, excludedSources);
            if (sessionPairs.Count == 0)
            {
                return basePromptOnlyGlossary;
            }

            return MergeLists(basePromptOnlyGlossary, sessionPairs);
        }

        public static HashSet<string> BuildExcludedSources(IReadOnlyList<(string Source, string Target)> basePromptOnlyGlossary)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (source, _) in basePromptOnlyGlossary)
            {
                var normalized = NormalizeSessionTermKey(source);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    excluded.Add(normalized);
                }
            }
            return excluded;
        }

        public IReadOnlyList<(string Source, string Token, string Target)> GetForcingEntriesForText(
            string text,
            HashSet<string> excludedSources
        )
        {
            if (_termToEntry.IsEmpty)
            {
                return Array.Empty<(string Source, string Token, string Target)>();
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<(string Source, string Token, string Target)>();
            }

            var list = new List<(string Source, string Token, string Target)>();

            foreach (var (source, entry) in _termToEntry)
            {
                if (!entry.AllowForce || excludedSources.Contains(source))
                {
                    continue;
                }

                if (!ContainsSessionTerm(text, source))
                {
                    continue;
                }

                list.Add((source, entry.Token, entry.Target));
            }

            if (list.Count == 0)
            {
                return list;
            }

            list.Sort((a, b) => b.Source.Length.CompareTo(a.Source.Length));
            if (list.Count > MaxSessionTermPairsPerRequest)
            {
                list.RemoveRange(MaxSessionTermPairsPerRequest, list.Count - MaxSessionTermPairsPerRequest);
            }

            return list;
        }

        private List<(string Source, string Target)> GetRelevantPairsForTexts(IReadOnlyList<string> texts, HashSet<string> excludedSources)
        {
            var list = new List<(string Source, string Target)>();

            foreach (var (source, entry) in _termToEntry)
            {
                if (entry.Conflicted || excludedSources.Contains(source))
                {
                    continue;
                }

                AddIfRelevant(source);
                // A small, explicit set of plural name forms is hint-only. Do not
                // infer single-word morphology (e.g. Trigger -> the verb triggers).
                var plural = GetSessionTermPluralHint(source);
                if (plural != null && !_termToEntry.ContainsKey(plural)) AddIfRelevant(plural);

                void AddIfRelevant(string variant)
                {
                    if (excludedSources.Contains(variant)) return;
                    foreach (var text in texts)
                    {
                        if (ContainsSessionTerm(text, variant))
                        {
                            list.Add((variant, entry.Target));
                            return;
                        }
                    }
                }
            }

            if (list.Count == 0)
            {
                return list;
            }

            list.Sort((a, b) =>
            {
                var length = b.Source.Length.CompareTo(a.Source.Length);
                return length != 0 ? length : StringComparer.OrdinalIgnoreCase.Compare(a.Source, b.Source);
            });
            if (list.Count > MaxSessionTermPairsPerRequest)
            {
                list.RemoveRange(MaxSessionTermPairsPerRequest, list.Count - MaxSessionTermPairsPerRequest);
            }

            return list;
        }

        private static IReadOnlyList<(string Source, string Target)> MergeLists(
            IReadOnlyList<(string Source, string Target)> basePairs,
            IReadOnlyList<(string Source, string Target)> extraPairs
        )
        {
            if (basePairs.Count == 0)
            {
                return extraPairs;
            }

            var merged = new List<(string Source, string Target)>(basePairs.Count + extraPairs.Count);
            merged.AddRange(basePairs);
            merged.AddRange(extraPairs);
            return merged;
        }
    }
}
