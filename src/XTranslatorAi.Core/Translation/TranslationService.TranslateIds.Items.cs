using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private async Task<List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> BuildTranslationItemsAsync(
        TranslateIdsRequest request,
        PlaceholderMasker placeholderMasker,
        GlossaryApplier glossaryApplier,
        IReadOnlyDictionary<string, string> translationMemory
    )
    {
        _rowContextById = new Dictionary<long, RowContext>(capacity: request.Ids.Count);

        var items = new List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>();
        var canonicalIdByMaskedText = new Dictionary<string, long>(StringComparer.Ordinal);
        var duplicateRowsByCanonicalId = new Dictionary<long, List<(long Id, string Source, MaskedText Mask)>>();
        var orderedRows = new List<DialogueContextRow>(capacity: request.Ids.Count);

        foreach (var id in request.Ids)
        {
            request.CancellationToken.ThrowIfCancellationRequested();

            var row = await GetRowContextByIdAsync(id, request.CancellationToken);
            _rowContextById[row.Id] = new RowContext(row.Rec, row.Edid);
            orderedRows.Add(new DialogueContextRow(row.Id, row.Rec, row.Edid, row.SourceText));

            if (row.Status != StringEntryStatus.Pending && row.Status != StringEntryStatus.Error)
            {
                continue;
            }

            if (await TryApplyTranslationMemoryAsync(
                    row.Id,
                    row.SourceText,
                    request.TargetLang,
                    translationMemory,
                    request.OnRowUpdated,
                    request.CancellationToken
                ))
            {
                continue;
            }

            var sourceForMask = PlaceholderUnitBinder.InjectUnitsForTranslation(request.TargetLang, row.SourceText);
            var masked = placeholderMasker.Mask(sourceForMask);
            var fortifyExpanded = FortifyListExpander.Expand(masked.Text);
            if (!string.Equals(fortifyExpanded, masked.Text, StringComparison.Ordinal))
            {
                masked = masked with { Text = fortifyExpanded };
            }
            var glossed = glossaryApplier.Apply(masked.Text);
            var expanded = PairedSlashListExpander.Expand(glossed.Text);
            if (!string.Equals(expanded, glossed.Text, StringComparison.Ordinal))
            {
                glossed = glossed with { Text = expanded };
            }

            var duplicateKey = expanded;
            if (glossed.TokenToReplacement.Count > 0)
            {
                var sb = new StringBuilder(capacity: expanded.Length + glossed.TokenToReplacement.Count * 24);
                sb.Append(expanded);
                sb.Append("\n__XT_GLOSSARY__");
                foreach (var (token, replacement) in glossed.TokenToReplacement.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    sb.Append('\n');
                    sb.Append(token);
                    sb.Append('=');
                    sb.Append(replacement);
                }

                duplicateKey = sb.ToString();
            }

            if (canonicalIdByMaskedText.TryGetValue(duplicateKey, out var canonicalId))
            {
                if (!duplicateRowsByCanonicalId.TryGetValue(canonicalId, out var dups))
                {
                    dups = new List<(long Id, string Source, MaskedText Mask)>();
                    duplicateRowsByCanonicalId[canonicalId] = dups;
                }

                dups.Add((row.Id, row.SourceText, masked));
                continue;
            }

            canonicalIdByMaskedText[duplicateKey] = row.Id;
            items.Add((row.Id, row.SourceText, expanded, masked, glossed));
        }

        _dialogueContextWindowById = _enableDialogueContextWindow
            ? BuildDialogueContextWindowMap(orderedRows)
            : null;

        if (duplicateRowsByCanonicalId.Count == 0)
        {
            _duplicateRowsByCanonicalId = null;
        }
        else
        {
            var frozen = new Dictionary<long, IReadOnlyList<(long Id, string Source, MaskedText Mask)>>(capacity: duplicateRowsByCanonicalId.Count);
            foreach (var (canonicalId, dups) in duplicateRowsByCanonicalId)
            {
                frozen[canonicalId] = dups.ToArray();
            }
            _duplicateRowsByCanonicalId = frozen;
        }

        return items;
    }

    private sealed record DialogueContextRow(long Id, string? Rec, string? Edid, string SourceText);

    private IReadOnlyDictionary<long, string> BuildDialogueContextWindowMap(IReadOnlyList<DialogueContextRow> orderedRows)
    {
        var map = new Dictionary<long, string>();
        if (orderedRows.Count == 0)
        {
            return map;
        }

        for (var index = 0; index < orderedRows.Count; index++)
        {
            var row = orderedRows[index];
            if (!IsDialogueRecBase(row.Rec))
            {
                continue;
            }

            var window = BuildDialogueContextWindowForIndex(orderedRows, index);
            if (!string.IsNullOrWhiteSpace(window))
            {
                map[row.Id] = window;
            }
        }

        return map;
    }

    private static bool IsDialogueRecBase(string? rec)
    {
        var baseRec = TranslationBatchGrouping.NormalizeRecBase(rec);
        return string.Equals(baseRec, "DIAL", StringComparison.OrdinalIgnoreCase)
               || string.Equals(baseRec, "INFO", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildDialogueContextWindowForIndex(IReadOnlyList<DialogueContextRow> orderedRows, int index)
    {
        const int prevCount = 2;
        const int nextCount = 1;
        const int maxLookaround = 40;

        var row = orderedRows[index];
        var edidStem = TranslationBatchGrouping.NormalizeEdidStem(row.Edid);

        var prev = new List<string>(capacity: prevCount);
        var next = new List<string>(capacity: nextCount);

        if (!string.IsNullOrWhiteSpace(edidStem))
        {
            CollectContextByEdidStem(orderedRows, index, edidStem, maxLookaround, prev, next);
        }
        else
        {
            CollectContextByAdjacency(orderedRows, index, prev, next);
        }

        if (prev.Count == 0 && next.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        if (prev.Count > 0)
        {
            sb.AppendLine("Prev (reference only):");
            foreach (var line in prev)
            {
                sb.Append("- ");
                sb.AppendLine(line);
            }
        }

        if (next.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            sb.AppendLine("Next (reference only):");
            foreach (var line in next)
            {
                sb.Append("- ");
                sb.AppendLine(line);
            }
        }

        return sb.ToString().Trim();
    }

    private static void CollectContextByEdidStem(
        IReadOnlyList<DialogueContextRow> orderedRows,
        int index,
        string edidStem,
        int maxLookaround,
        List<string> prev,
        List<string> next
    )
    {
        for (var i = index - 1; i >= 0 && prev.Count < 2; i--)
        {
            if (index - i > maxLookaround)
            {
                break;
            }

            var candidate = orderedRows[i];
            if (!IsDialogueRecBase(candidate.Rec))
            {
                continue;
            }

            var stem = TranslationBatchGrouping.NormalizeEdidStem(candidate.Edid);
            if (!string.Equals(stem, edidStem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var line = TrySanitizeDialogueContextLine(candidate.SourceText);
            if (line != null)
            {
                prev.Add(line);
            }
        }
        prev.Reverse();

        for (var i = index + 1; i < orderedRows.Count && next.Count < 1; i++)
        {
            if (i - index > maxLookaround)
            {
                break;
            }

            var candidate = orderedRows[i];
            if (!IsDialogueRecBase(candidate.Rec))
            {
                continue;
            }

            var stem = TranslationBatchGrouping.NormalizeEdidStem(candidate.Edid);
            if (!string.Equals(stem, edidStem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var line = TrySanitizeDialogueContextLine(candidate.SourceText);
            if (line != null)
            {
                next.Add(line);
            }
        }
    }

    private static void CollectContextByAdjacency(
        IReadOnlyList<DialogueContextRow> orderedRows,
        int index,
        List<string> prev,
        List<string> next
    )
    {
        for (var i = index - 1; i >= 0 && prev.Count < 2; i--)
        {
            var candidate = orderedRows[i];
            if (!IsDialogueRecBase(candidate.Rec))
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(TranslationBatchGrouping.NormalizeEdidStem(candidate.Edid)))
            {
                break;
            }

            var line = TrySanitizeDialogueContextLine(candidate.SourceText);
            if (line != null)
            {
                prev.Add(line);
            }
        }
        prev.Reverse();

        for (var i = index + 1; i < orderedRows.Count && next.Count < 1; i++)
        {
            var candidate = orderedRows[i];
            if (!IsDialogueRecBase(candidate.Rec))
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(TranslationBatchGrouping.NormalizeEdidStem(candidate.Edid)))
            {
                break;
            }

            var line = TrySanitizeDialogueContextLine(candidate.SourceText);
            if (line != null)
            {
                next.Add(line);
            }
        }
    }

    private static string? TrySanitizeDialogueContextLine(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return null;
        }

        if (sourceText.IndexOf("__XT_", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return null;
        }

        if (sourceText.IndexOf('%', StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        if (sourceText.IndexOf('{', StringComparison.Ordinal) >= 0
            || sourceText.IndexOf('}', StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        if (RawMarkupTagRegex.IsMatch(sourceText) || RawPagebreakRegex.IsMatch(sourceText))
        {
            return null;
        }

        var collapsed = CollapseWhitespace(sourceText).Trim();
        if (collapsed.Length == 0)
        {
            return null;
        }

        const int maxLen = 180;
        if (collapsed.Length > maxLen)
        {
            collapsed = collapsed[..maxLen].Trim();
        }

        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(capacity: text.Length);
        var wasWhitespace = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                if (!wasWhitespace)
                {
                    sb.Append(' ');
                    wasWhitespace = true;
                }
                continue;
            }

            sb.Append(ch);
            wasWhitespace = false;
        }

        return sb.ToString();
    }

    private async Task<List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> SeedSessionTermMemoryAsync(
        TranslateIdsRequest request,
        List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items,
        PromptCache? promptCache,
        PlaceholderMasker placeholderMasker,
        System.Text.Json.JsonElement responseSchema
    )
    {
        if (!_enableSessionTermMemory)
        {
            return items;
        }

        var seedItems = SelectSessionTermSeedItems(items, DefaultSessionTermSeedCount);
        if (seedItems.Count == 0)
        {
            return items;
        }

        foreach (var batch in TranslationBatching.ChunkBy(seedItems, it => it.Masked.Length, request.BatchSize, request.MaxChars))
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            if (request.WaitIfPaused != null)
            {
                await request.WaitIfPaused(request.CancellationToken);
            }

            await MarkInProgressAsync(batch, request.CancellationToken, request.OnRowUpdated);
            var ctx = new PipelineContext(
                ApiKey: request.ApiKey,
                ModelName: request.ModelName,
                SystemPrompt: request.SystemPrompt,
                EnableApiKeyFailover: request.EnableApiKeyFailover,
                PromptCache: promptCache,
                SourceLang: request.SourceLang,
                TargetLang: request.TargetLang,
                MaxChars: request.MaxChars,
                Temperature: request.Temperature,
                MaxOutputTokens: request.MaxOutputTokens,
                ResponseSchema: responseSchema,
                MaxRetries: request.MaxRetries,
                EnableRepairPass: request.EnableRepairPass,
                PlaceholderMasker: placeholderMasker,
                OnRowUpdated: request.OnRowUpdated,
                CancellationToken: request.CancellationToken
            );
            await TranslateBatchWithSplitFallbackAsync(ctx, batch);
            await FlushSessionTermAutoGlossaryInsertsAsync();
        }

        var doneSeedIds = new HashSet<long>();
        foreach (var it in seedItems)
        {
            var state = await _db.GetStringTranslationStateAsync(it.Id, request.CancellationToken);
            if (state.Status == StringEntryStatus.Done || state.Status == StringEntryStatus.Edited)
            {
                doneSeedIds.Add(it.Id);
            }
        }

        if (doneSeedIds.Count == 0)
        {
            return items;
        }

        var remaining = new List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>(
            capacity: Math.Max(0, items.Count - doneSeedIds.Count)
        );
        foreach (var it in items)
        {
            if (!doneSeedIds.Contains(it.Id))
            {
                remaining.Add(it);
            }
        }
        return remaining;
    }
}
