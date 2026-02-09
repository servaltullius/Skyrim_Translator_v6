using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private sealed class SessionTermMemory
    {
        private sealed record SessionTermEntry(string Target, string Token);

        private readonly ConcurrentDictionary<string, SessionTermEntry> _termToEntry;
        private readonly int _maxTerms;
        private int _nextTokenId = -1;

        public SessionTermMemory(int maxTerms)
        {
            _maxTerms = Math.Max(0, maxTerms);
            _termToEntry = new ConcurrentDictionary<string, SessionTermEntry>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsEmpty => _termToEntry.IsEmpty;

        public bool TryLearn(string sourceTerm, string targetTranslation)
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

            // Bound growth: keep the first mapping for consistency.
            if (_termToEntry.Count >= _maxTerms && !_termToEntry.ContainsKey(key))
            {
                return false;
            }

            var tokenId = Interlocked.Increment(ref _nextTokenId);
            var token = $"__XT_TERM_SESS_{tokenId:0000}__";
            return _termToEntry.TryAdd(key, new SessionTermEntry(target, token));
        }

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
                if (excludedSources.Contains(source))
                {
                    continue;
                }

                if (text.IndexOf(source, StringComparison.OrdinalIgnoreCase) < 0)
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
                if (excludedSources.Contains(source))
                {
                    continue;
                }

                var hit = false;
                foreach (var t in texts)
                {
                    if (string.IsNullOrWhiteSpace(t))
                    {
                        continue;
                    }

                    if (t.IndexOf(source, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hit = true;
                        break;
                    }
                }

                if (hit)
                {
                    list.Add((source, entry.Target));
                }
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

