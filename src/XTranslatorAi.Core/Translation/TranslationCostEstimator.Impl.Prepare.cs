using System.Collections.Generic;
using System.Threading;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationCostEstimator
{
    private sealed record PreparedEstimationInput(
        List<PreparedRow> Items,
        Dictionary<long, (string? Rec, string? Edid)> ContextById,
        long TotalSourceChars,
        long TotalMaskedChars
    );

    private sealed record PreparedRow(
        long Id,
        string Source,
        string Masked,
        IReadOnlyList<(string Source, string Target)> PromptOnlyPairs
    );

    private static PreparedEstimationInput PrepareEstimationRows(
        IReadOnlyList<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)> rows,
        string targetLang,
        PlaceholderMasker placeholderMasker,
        GlossaryApplier glossaryApplier,
        CancellationToken cancellationToken
    )
    {
        var items = new List<PreparedRow>(capacity: rows.Count);
        var ctxById = new Dictionary<long, (string? Rec, string? Edid)>(capacity: rows.Count);
        long totalSourceChars = 0;
        long totalMaskedChars = 0;

        foreach (var (id, sourceText, rec, edid, _) in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalSourceChars += sourceText.Length;
            ctxById[id] = (rec, edid);

            var sourceForMask = PlaceholderUnitBinder.InjectUnitsForTranslation(targetLang, sourceText);
            var masked = placeholderMasker.Mask(sourceForMask);
            var glossed = glossaryApplier.Apply(masked.Text);
            var expanded = PairedSlashListExpander.Expand(glossed.Text);
            totalMaskedChars += expanded.Length;

            items.Add(new PreparedRow(id, sourceText, expanded, glossed.PromptOnlyPairs));
        }

        return new PreparedEstimationInput(items, ctxById, totalSourceChars, totalMaskedChars);
    }
}
