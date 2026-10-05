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
        Ctx.RowContextById = new Dictionary<long, RowContext>(capacity: request.Ids.Count);
        var rowsById = await _db.GetStringTranslationContextsByIdsAsync(request.Ids, request.CancellationToken);
        Ctx.BookTitlesByEdid = request.BookTitlesByEdid ?? (Ctx.EnableBookContext
            ? TranslationBookContext.CollectTitles(rowsById.Values.Select(row => (row.Rec, row.Edid, row.SourceText)))
            : null);
        var previousTranslations = await _db.GetPreviousTranslationsByStringIdAsync(request.CancellationToken);
        Ctx.PreviousTranslationById = previousTranslations.Count == 0 ? null : previousTranslations;

        var items = new List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>();
        var canonicalIdByMaskedText = new Dictionary<DuplicateKey, long>();
        var duplicateRowsByCanonicalId = new Dictionary<long, List<(long Id, string Source, MaskedText Mask)>>();
        var orderedRows = new List<DialogueContextRow>(capacity: request.Ids.Count);

        foreach (var id in request.Ids.Distinct())
        {
            request.CancellationToken.ThrowIfCancellationRequested();

            if (!rowsById.TryGetValue(id, out var row))
            {
                throw new InvalidOperationException($"Missing row id={id}");
            }

            Ctx.RowContextById[row.Id] = new RowContext(row.Rec, row.Edid);
            orderedRows.Add(new DialogueContextRow(row.Id, row.Rec, row.Edid, row.SourceText, row.DialogueScope));
        }

        Ctx.DialogueContextWindowById = Ctx.EnableDialogueContextWindow
            ? BuildDialogueContextWindowMap(orderedRows)
            : null;
        var ambiguousTmSources = FindAmbiguousTranslationMemorySources(orderedRows);

        foreach (var contextRow in orderedRows)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            var row = rowsById[contextRow.Id];

            if (row.Status != StringEntryStatus.Pending && row.Status != StringEntryStatus.Error)
            {
                continue;
            }

            if (!HasLettersToTranslate(row.SourceText)
                || XTranslatorAi.Core.Text.Lqa.Internal.Rules.HiddenTopicRule.IsHiddenTopicIdentifier(row.Rec, row.SourceText))
            {
                // "...", a blank, "???", "11", an empty paragraph tag: nothing to translate, and a blank answer for a
                // blank source could not be saved in a required field. A hidden topic's identifier is never shown,
                // and the quality check asks to leave it as is.
                await _db.UpdateStringTranslationAsync(row.Id, row.SourceText, StringEntryStatus.Done, null, request.CancellationToken);
                if (request.OnRowUpdated != null)
                {
                    NotifyRowUpdated(request.OnRowUpdated, row.Id, StringEntryStatus.Done, row.SourceText);
                }

                continue;
            }

            var sourceKey = TranslationMemoryKey.NormalizeSource(row.SourceText);
            var hasUnscopedDialogueTm = contextRow.DialogueScope != null && translationMemory.ContainsKey(sourceKey);
            if (hasUnscopedDialogueTm || (ambiguousTmSources.Contains(sourceKey) && translationMemory.ContainsKey(sourceKey)))
            {
                await _db.UpsertStringNoteAsync(row.Id, TranslationConstants.TmFallbackNoteKind,
                    hasUnscopedDialogueTm
                        ? "TM 폴백: 직접 플러그인의 INFO/DIAL 대화는 기존 원문 전용 TM에서 topic 출처를 확인할 수 없어 개별 번역합니다. 기존 TM과 완료/편집 번역은 유지합니다."
                        : "TM 폴백: 같은 원문의 REC/EDID/대화 문맥이 달라 개별 번역합니다.", request.CancellationToken);
            }
            else if (await TryApplyTranslationMemoryAsync(
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
            var glossed = Ctx.ReferenceNames?.ApplyWithGlossary(masked.Text, glossaryApplier) ?? glossaryApplier.Apply(masked.Text);
            var expanded = PairedSlashListExpander.Expand(glossed.Text);
            if (!string.Equals(expanded, glossed.Text, StringComparison.Ordinal))
            {
                glossed = glossed with { Text = expanded };
            }

            var duplicateKey = new DuplicateKey(
                row.SourceText,
                BuildDuplicateKey(expanded, glossed.TokenToReplacement),
                GetTranslationContextKey(contextRow)
            );

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

        if (duplicateRowsByCanonicalId.Count == 0)
        {
            Ctx.DuplicateRowsByCanonicalId = null;
        }
        else
        {
            var frozen = new Dictionary<long, IReadOnlyList<(long Id, string Source, MaskedText Mask)>>(capacity: duplicateRowsByCanonicalId.Count);
            foreach (var (canonicalId, dups) in duplicateRowsByCanonicalId)
            {
                frozen[canonicalId] = dups.ToArray();
            }
            Ctx.DuplicateRowsByCanonicalId = frozen;
        }

        return items;
    }

    // Reuse a translation only when both its protected source and prompt context
    // agree. Identical words may be an item name, a command, or different dialogue.
    private readonly record struct TranslationContextKey(string Rec, string Edid, string Dialogue, string Scope);
    private readonly record struct DuplicateKey(string Source, string TextAndGlossary, TranslationContextKey Context);

    // Outside dialogue the EditorID usually only names the record: "Fortify Mystic" on seven perks
    // (MagicSkillPerk01-07) was translated seven times and came out as 신비 강화 and 강화 신비. Same source and record
    // type now means one translation. Dialogue keeps the EditorID, since a line's meaning depends on its topic, and so
    // do races and NPCs, whose EditorID tells which one a generic name means (巨人 is the Giant on GiantRace and the
    // Lurker on DLC2LurkerRace).
    private TranslationContextKey GetTranslationContextKey(DialogueContextRow row)
        => new(row.Rec?.Trim().ToUpperInvariant() ?? "",
            KeepsEditorIdInContext(row) ? row.Edid?.Trim().ToUpperInvariant() ?? "" : "",
            GetDialogueContextWindowForId(row.Id) ?? "", row.DialogueScope ?? "");

    private static bool KeepsEditorIdInContext(DialogueContextRow row)
        => IsDialogueRecBase(row.Rec) || row.DialogueScope != null
           || (row.Rec ?? "").TrimStart().StartsWith("RACE", StringComparison.OrdinalIgnoreCase)
           || (row.Rec ?? "").TrimStart().StartsWith("NPC_", StringComparison.OrdinalIgnoreCase);

    private HashSet<string> FindAmbiguousTranslationMemorySources(IReadOnlyList<DialogueContextRow> rows)
    {
        // The current TM schema is source-only. When the selected rows prove that
        // a source belongs to several contexts, do not choose one TM value for all.
        var firstContextBySource = new Dictionary<string, TranslationContextKey>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var source = TranslationMemoryKey.NormalizeSource(row.SourceText);
            var context = GetTranslationContextKey(row);
            if (firstContextBySource.TryGetValue(source, out var previous) && previous != context)
            {
                ambiguous.Add(source);
            }
            else
            {
                firstContextBySource[source] = context;
            }
        }
        return ambiguous;
    }

    private static string BuildDuplicateKey(string expandedText, IReadOnlyDictionary<string, string> glossaryTokenToReplacement)
    {
        if (glossaryTokenToReplacement.Count <= 0)
        {
            return expandedText;
        }

        var sb = new StringBuilder(capacity: expandedText.Length + glossaryTokenToReplacement.Count * 24);
        sb.Append(expandedText);
        sb.Append("\n__XT_GLOSSARY__");
        foreach (var (token, replacement) in glossaryTokenToReplacement.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append('\n');
            sb.Append(token);
            sb.Append('=');
            sb.Append(replacement);
        }

        return sb.ToString();
    }

    private sealed record DialogueContextRow(long Id, string? Rec, string? Edid, string SourceText, string? DialogueScope);

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

        if (row.DialogueScope != null)
        {
            CollectContextByPluginScope(orderedRows, index, row.DialogueScope, maxLookaround, prev, next);
        }
        else if (!string.IsNullOrWhiteSpace(edidStem))
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

    private static void CollectContextByPluginScope(
        IReadOnlyList<DialogueContextRow> orderedRows, int index, string scope, int maxLookaround,
        List<string> prev, List<string> next)
    {
        for (var i = index - 1; i >= Math.Max(0, index - maxLookaround) && prev.Count < 2; i--)
        {
            var candidate = orderedRows[i];
            if (candidate.DialogueScope != scope || !IsDialogueRecBase(candidate.Rec)) continue;
            var line = TrySanitizeDialogueContextLine(candidate.SourceText);
            if (line != null) prev.Add(line);
        }
        prev.Reverse();
        for (var i = index + 1; i < Math.Min(orderedRows.Count, index + maxLookaround + 1) && next.Count < 1; i++)
        {
            var candidate = orderedRows[i];
            if (candidate.DialogueScope != scope || !IsDialogueRecBase(candidate.Rec)) continue;
            var line = TrySanitizeDialogueContextLine(candidate.SourceText);
            if (line != null) next.Add(line);
        }
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
            if (!IsDialogueRecBase(candidate.Rec) || candidate.DialogueScope != null)
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
            if (!IsDialogueRecBase(candidate.Rec) || candidate.DialogueScope != null)
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
            if (!IsDialogueRecBase(candidate.Rec) || candidate.DialogueScope != null)
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
            if (!IsDialogueRecBase(candidate.Rec) || candidate.DialogueScope != null)
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

        if (TokenSanitizer.RawMarkupTagRegex.IsMatch(sourceText) || TokenSanitizer.RawPagebreakRegex.IsMatch(sourceText))
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
        if (!Ctx.EnableSessionTermMemory)
        {
            return items;
        }

        await SeedSubtermsAsync(request, promptCache, placeholderMasker, responseSchema, items);
        await SeedRepeatedSentencesAsync(request, promptCache, placeholderMasker, responseSchema, items);

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
            try
            {
                await TranslateBatchWithSplitFallbackAsync(ctx, batch);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (IsRunGenerationLimit(ex) || IsRateLimitAbort(ex) || IsCredentialError(ex)
                    || (request.EnableApiKeyFailover && IsApiKeyFailoverError(ex, request.CancellationToken)))
                    throw;
                ThrowIfRateLimitStreakReached(ex, request.EnableApiKeyFailover);
                await HandleBatchFailureAsync(request, batch, ex, request.CancellationToken);
            }
            await FlushSessionTermAutoGlossaryInsertsAsync();
        }

        var seedIds = new long[seedItems.Count];
        for (var i = 0; i < seedItems.Count; i++)
        {
            seedIds[i] = seedItems[i].Id;
        }

        var seedStatuses = await _db.GetStringStatusesByIdsAsync(seedIds, request.CancellationToken);
        var finishedSeedIds = new HashSet<long>();
        foreach (var it in seedItems)
        {
            if (!seedStatuses.TryGetValue(it.Id, out var status))
            {
                continue;
            }

            if (status is StringEntryStatus.Done or StringEntryStatus.Edited or StringEntryStatus.Error)
            {
                finishedSeedIds.Add(it.Id);
            }
        }

        if (finishedSeedIds.Count == 0)
        {
            return items;
        }

        var remaining = new List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>(
            capacity: Math.Max(0, items.Count - finishedSeedIds.Count)
        );
        foreach (var it in items)
        {
            if (!finishedSeedIds.Contains(it.Id))
            {
                remaining.Add(it);
            }
        }
        return remaining;
    }
}
