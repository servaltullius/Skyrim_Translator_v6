using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.Converters;

/// <summary>Shows Korean names for enum values in lists and grids; the bound value stays the enum.</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    private static readonly Dictionary<Enum, string> Names = new()
    {
        [GlossaryMatchMode.WordBoundary] = "단어 단위",
        [GlossaryMatchMode.Substring] = "부분 일치",
        [GlossaryMatchMode.Regex] = "정규식",
        [GlossaryForceMode.ForceToken] = "강제 적용",
        [GlossaryForceMode.PromptOnly] = "참고 힌트",
    };

    public static string ToDisplay(object? value)
        => value is Enum e && Names.TryGetValue(e, out var name) ? name : value?.ToString() ?? "";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => ToDisplay(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Display names are not converted back; bind SelectedItem to the enum.");
}
