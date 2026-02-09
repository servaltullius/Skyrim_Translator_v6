namespace XTranslatorAi.Core.Text.KoreanFix.Internal;

internal interface IKoreanFixStep
{
    string Apply(KoreanFixContext context, string text);
}
