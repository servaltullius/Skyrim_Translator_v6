namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

/// <summary>
/// Last, after the steps that rewrite a duration ("&lt;dur&gt;초 동안 …"): a placeholder's seconds stand apart, as the
/// Korean patch writes them. See <see cref="PlaceholderSecondsSpacing"/>.
/// </summary>
internal sealed class PlaceholderSecondsSpacingStep : IKoreanFixStep
{
    public string Apply(KoreanFixContext context, string text) => PlaceholderSecondsSpacing.Apply(text);
}
