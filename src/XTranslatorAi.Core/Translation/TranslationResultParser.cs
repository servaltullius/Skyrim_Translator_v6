using System;
using System.Collections.Generic;
using System.Text.Json;

namespace XTranslatorAi.Core.Translation;

public static class TranslationResultParser
{
    public static IReadOnlyDictionary<long, string> ParseTranslations(string modelText)
    {
        var raw = NormalizeModelText(modelText);
        using var doc = ParseJson(raw);
        var translations = GetTranslationsArray(doc.RootElement);
        return BuildTranslationMap(translations);
    }

    private static string NormalizeModelText(string modelText)
    {
        var raw = modelText.Trim();
        return raw.StartsWith("```", StringComparison.Ordinal) ? StripCodeFence(raw) : raw;
    }

    private static JsonDocument ParseJson(string raw)
    {
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            var extracted = ExtractJsonObjectOrArray(raw);
            return JsonDocument.Parse(extracted);
        }
    }

    private static JsonElement GetTranslationsArray(JsonElement root)
    {
        if (!root.TryGetProperty("translations", out var translations) || translations.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Model JSON missing 'translations' array.");
        }

        return translations;
    }

    private static IReadOnlyDictionary<long, string> BuildTranslationMap(JsonElement translations)
    {
        var map = new Dictionary<long, string>();
        foreach (var item in translations.EnumerateArray())
        {
            if (TryReadTranslationItem(item, out var id, out var text))
            {
                map[id] = text;
            }
        }

        return map;
    }

    private static bool TryReadTranslationItem(JsonElement item, out long id, out string text)
    {
        id = 0;
        text = "";

        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
        {
            return false;
        }
        if (!item.TryGetProperty("text", out var textEl) || textEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        id = idEl.GetInt64();
        text = textEl.GetString() ?? "";
        return true;
    }

    private static string StripCodeFence(string raw)
    {
        var s = raw;
        var firstNewline = s.IndexOf('\n');
        if (firstNewline >= 0)
        {
            s = s[(firstNewline + 1)..];
        }
        if (s.EndsWith("```", StringComparison.Ordinal))
        {
            s = s[..^3];
        }
        return s.Trim();
    }

    private static string ExtractJsonObjectOrArray(string raw)
    {
        var firstBrace = raw.IndexOf('{');
        var firstBracket = raw.IndexOf('[');
        var start = -1;
        if (firstBrace >= 0 && firstBracket >= 0)
        {
            start = Math.Min(firstBrace, firstBracket);
        }
        else
        {
            start = Math.Max(firstBrace, firstBracket);
        }
        if (start < 0)
        {
            throw new InvalidOperationException("Model output did not contain JSON.");
        }

        var lastBrace = raw.LastIndexOf('}');
        var lastBracket = raw.LastIndexOf(']');
        var end = Math.Max(lastBrace, lastBracket);
        if (end <= start)
        {
            throw new InvalidOperationException("Model output did not contain complete JSON.");
        }

        return raw[start..(end + 1)];
    }
}
