namespace XTranslatorAi.Core.Text.KoreanFix.Internal;

internal sealed class KoreanFixContext
{
    public KoreanFixContext(string targetLang)
    {
        TargetLang = targetLang;
    }

    public string TargetLang { get; }
}
