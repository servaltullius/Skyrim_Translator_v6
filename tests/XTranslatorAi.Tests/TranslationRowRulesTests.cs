using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

/// <summary>
/// The row rules the translation, the quality check and agent mode share (TranslationRowRules). Their copies read a Rec
/// differently: exactly, or trimmed and ignoring case. These pin how a Rec is read now, whatever its spacing and case.
/// </summary>
public sealed class TranslationRowRulesTests
{
    [Theory]
    [InlineData("INFO:RNAM", "INFO", "RNAM")]
    [InlineData(" info:rnam ", "INFO", "RNAM")]
    [InlineData("BOOK:DESC", "BOOK", "DESC")]
    [InlineData(" book : desc ", "BOOK", "DESC")]
    [InlineData("INFO :RNAM", "INFO", "RNAM")]
    [InlineData("npc_:full", "NPC_", "FULL")]
    [InlineData("WEAP", "WEAP", null)]
    [InlineData("QUST:", "QUST", null)]
    [InlineData("QUST: ", "QUST", null)]
    [InlineData(":FULL", ":FULL", "FULL")]
    [InlineData("", "", null)]
    [InlineData(null, "", null)]
    public void RecordAndFieldType(string? rec, string recordType, string? fieldType)
    {
        Assert.Equal(recordType, TranslationRowRules.RecordType(rec));
        Assert.Equal(fieldType, TranslationRowRules.FieldType(rec));
    }

    [Theory]
    [InlineData("INFO:RNAM", true, false)]
    [InlineData(" dial:full ", true, false)]
    [InlineData("INFO : RNAM", true, false)]
    [InlineData("INFO:NAM1", false, true)]
    [InlineData("info: nam1", false, true)]
    [InlineData("DIAL:NAM1", false, false)]
    [InlineData("INFO", false, false)]
    [InlineData(null, false, false)]
    public void PlayerAndNpcLine(string? rec, bool player, bool npc)
    {
        Assert.Equal(player, TranslationRowRules.IsPlayerLine(rec));
        Assert.Equal(npc, TranslationRowRules.IsNpcLine(rec));
    }

    [Theory]
    [InlineData("DIAL/01000800/0/FULL/0", true)]
    [InlineData("INFO/01000801/0/RNAM/0", true)]
    [InlineData("INFO/01000801/0/NAM1/0", false)]
    [InlineData(null, false)]
    public void PlayerFieldKey(string? key, bool player) => Assert.Equal(player, TranslationRowRules.IsPlayerFieldKey(key));

    [Theory]
    [InlineData("INFO:NAM1", true, true)]
    [InlineData("DIAL:FULL", true, true)]
    [InlineData("RACE:FULL", false, true)]
    [InlineData("NPC_:FULL", false, true)]
    [InlineData("RACE", false, true)]
    [InlineData("INFO", true, true)]
    [InlineData("PERK:FULL", false, false)]
    [InlineData("BOOK:DESC", false, false)]
    public void DialogueAndEditorId(string rec, bool dialogue, bool keepsEditorId)
    {
        Assert.Equal(dialogue, TranslationRowRules.IsDialogue(rec));
        Assert.Equal(keepsEditorId, TranslationRowRules.KeepsEditorId(rec));
    }

    [Theory]
    [InlineData("BOOK:DESC", true)]
    [InlineData(" book : desc ", true)]
    [InlineData("BOOK:FULL", false)]
    [InlineData("MISC:DESC", false)]
    public void BookBody(string rec, bool body) => Assert.Equal(body, TranslationRowRules.IsBookBody(rec));
}
