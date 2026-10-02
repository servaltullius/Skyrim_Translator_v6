using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Plugins;

/// <summary>
/// Pairs the fields of the plugin being translated with the same fields of an earlier translated release
/// of it (field keys carry FormID, record, subrecord and occurrence). An earlier text is kept only when it
/// is actually a translation: not empty, not identical to the current source, and, for Korean, containing Hangul.
/// </summary>
public static class PreviousTranslationMatcher
{
    public sealed record Result(
        IReadOnlyDictionary<string, string> TextByFieldKey,
        int NotInPreviousRelease,
        int SameAsSource,
        int NotTranslated
    );

    public static Result Match(IReadOnlyList<PluginField> current, IReadOnlyList<PluginField> previous, bool requireHangul)
    {
        var previousByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in previous)
        {
            previousByKey.TryAdd(field.Key, field.SourceText);
        }

        var matched = new Dictionary<string, string>(StringComparer.Ordinal);
        int missing = 0, same = 0, untranslated = 0;
        foreach (var field in current)
        {
            if (!previousByKey.TryGetValue(field.Key, out var text) || string.IsNullOrWhiteSpace(text))
            {
                missing++;
                continue;
            }

            text = text.Trim();
            if (string.Equals(text, field.SourceText.Trim(), StringComparison.Ordinal))
            {
                same++;
                continue;
            }

            if (requireHangul && !text.Any(KoreanSyllables.IsHangulSyllable))
            {
                untranslated++;
                continue;
            }

            matched[field.Key] = text;
        }

        return new Result(matched, missing, same, untranslated);
    }
}
