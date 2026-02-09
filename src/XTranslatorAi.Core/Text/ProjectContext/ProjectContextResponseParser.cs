using System;
using System.Text.Json;

namespace XTranslatorAi.Core.Text.ProjectContext;

public static class ProjectContextResponseParser
{
    public static bool TryParseContext(string modelText, out string context)
    {
        context = "";

        if (string.IsNullOrWhiteSpace(modelText))
        {
            return false;
        }

        var raw = modelText.Trim();

        if (TryParseFromJson(raw, out context))
        {
            return !string.IsNullOrWhiteSpace(context);
        }

        return false;
    }

    private static bool TryParseFromJson(string raw, out string context)
    {
        context = "";

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return TryFindContextString(doc.RootElement, out context);
        }
        catch (JsonException)
        {
            // fallthrough
        }
        catch
        {
            return false;
        }

        try
        {
            var extracted = ExtractJsonObjectOrArray(raw);
            using var doc = JsonDocument.Parse(extracted);
            return TryFindContextString(doc.RootElement, out context);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFindContextString(JsonElement element, out string context)
    {
        context = "";

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String
                        && string.Equals(prop.Name, "context", StringComparison.OrdinalIgnoreCase))
                    {
                        context = prop.Value.GetString() ?? "";
                        return true;
                    }

                    if (TryFindContextString(prop.Value, out context))
                    {
                        return true;
                    }
                }

                return false;
            }

            case JsonValueKind.Array:
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindContextString(item, out context))
                    {
                        return true;
                    }
                }

                return false;
            }

            default:
                return false;
        }
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

