using System;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.ViewModels;

public partial class StringEntryViewModel
{
    public PluginField? PluginLocation { get; init; }

    /// <summary>The same field's text in the earlier translated release the project refers to, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviousTranslationText))]
    private string? _previousTranslation;

    public string PreviousTranslationText => string.IsNullOrWhiteSpace(PreviousTranslation) ? "" : "이전 번역: " + PreviousTranslation.Trim();

    public string PluginLocationText => PluginLocation is not { } location ? ""
        : $"FormID {location.FormId:X8} · {location.Rec}"
          + (location.TableKind is { } table && location.StringId is { } id ? $" · {table.ToString().ToUpperInvariant()} StringID {id}" : "");

    public string RowToolTip => string.Join("\n",
        new[] { PluginLocationText, IsSourceFix ? "원문 오류 수정으로 표시됨" : "", UserFacingErrorMessage }.Where(line => line.Length > 0));

    // Explicit selectors use exact identity rather than matching the digits in unrelated source text.
    public bool TryMatchLocationQuery(string query, out bool matches)
    {
        matches = false;
        if (query.StartsWith("string:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = query[7..].Split('/');
            matches = parts.Length == 2 && Enum.TryParse<PluginStringTableKind>(parts[0], true, out var table)
                && Enum.IsDefined(table) && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && PluginLocation?.TableKind == table && PluginLocation.StringId == id;
            return true;
        }
        if (query.StartsWith("form:", StringComparison.OrdinalIgnoreCase))
        {
            var hex = query[5..];
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
            matches = uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var formId)
                && PluginLocation?.FormId == formId;
            return true;
        }
        if (query.StartsWith("row:", StringComparison.OrdinalIgnoreCase))
        {
            matches = int.TryParse(query[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var index) && OrderIndex == index;
            return true;
        }
        return false;
    }
}
