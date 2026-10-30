using System;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// What a row's record tells about it (its Rec, "INFO:RNAM"; its plugin field key), for the translation, the quality
/// check and agent mode alike. Each had its own copies, and they drifted: the player's line was read in six places, one
/// trimming and ignoring case and another matching exactly; the records whose EDID tells what a text means were listed
/// three times, so one added to the translation's list alone would have had the check report the difference it meant;
/// the record type was cut from the Rec in seven ways.
/// </summary>
public static class TranslationRowRules
{
    /// <summary>The record type of a Rec ("INFO" of "INFO:RNAM"), upper case; "" for none.</summary>
    public static string RecordType(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return "";
        }

        var trimmed = rec.Trim();
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        return (colon > 0 ? trimmed[..colon].TrimEnd() : trimmed).ToUpperInvariant();
    }

    /// <summary>The field of a Rec ("RNAM" of "INFO:RNAM"), upper case; null without a colon or a field after it.</summary>
    public static string? FieldType(string? rec)
    {
        var colon = rec?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        var field = colon < 0 ? "" : rec![(colon + 1)..].Trim();
        return field.Length == 0 ? null : field.ToUpperInvariant();
    }

    /// <summary>A topic's or an info's text: a line of dialogue.</summary>
    public static bool IsDialogue(string? rec) => RecordType(rec) is "INFO" or "DIAL";

    /// <summary>
    /// The player's line: an INFO's prompt (RNAM), the line the player picks, and a topic's text (DIAL:FULL), both said
    /// to the NPC who answers. The speaker an INFO's conditions name is that NPC, not who says the line.
    /// </summary>
    public static bool IsPlayerLine(string? rec) => (RecordType(rec), FieldType(rec)) is ("INFO", "RNAM") or ("DIAL", "FULL");

    /// <summary>An NPC's line: an INFO's response (NAM1), said by the speaker its conditions name.</summary>
    public static bool IsNpcLine(string? rec) => (RecordType(rec), FieldType(rec)) is ("INFO", "NAM1");

    /// <summary>A book's text (BOOK:DESC), as opposed to its title.</summary>
    public static bool IsBookBody(string? rec) => (RecordType(rec), FieldType(rec)) is ("BOOK", "DESC");

    /// <summary>The player's line by a plugin row's field key ("DIAL/…", "INFO/…/RNAM/…"), where the Rec is not at hand.</summary>
    public static bool IsPlayerFieldKey(string? fieldKey)
        => fieldKey != null && (fieldKey.StartsWith("DIAL/", StringComparison.Ordinal) || fieldKey.Contains("/RNAM/", StringComparison.Ordinal));

    /// <summary>
    /// Records whose EDID tells what their text means, so the same English is not one translation across them:
    /// dialogue (a line's meaning depends on its topic), races and NPCs (巨人 is the Giant on GiantRace and the Lurker on
    /// DLC2LurkerRace). Elsewhere the EDID only names the record ("Fortify Mystic" on seven perks is one translation),
    /// and the quality check reports the same English translated two ways.
    /// </summary>
    public static bool KeepsEditorId(string? rec) => RecordType(rec) is "INFO" or "DIAL" or "RACE" or "NPC_";
}
