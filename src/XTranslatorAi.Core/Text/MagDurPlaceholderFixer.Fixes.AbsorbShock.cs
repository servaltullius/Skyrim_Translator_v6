using System;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static string? TryFixAbsorbAndShockTemplates(string source, string trimmedSource)
    {
        var absorb = AbsorbAttrPerSecondFromTargetRegex.Match(trimmedSource);
        if (absorb.Success)
        {
            var mag = absorb.Groups["mag"].Value.Trim();
            var attr = absorb.Groups["attr"].Value.Trim();
            var attrKo = TranslateKnownSubjectKo(attr);
            return $"대상에게서 {attrKo}{GetObjectParticle(attrKo)} 초당 {mag}포인트 흡수합니다.";
        }

        var shock = ShockDamagePerSecondToHealthAndMagickaRegex.Match(trimmedSource);
        if (shock.Success)
        {
            var name = shock.Groups["name"].Value.Trim();
            var nameKo = KnownNamesKo.TryGetValue(name, out var ko) ? ko : name;
            var magToken = shock.Groups["mag"].Value.Trim();
            var hasDisintegrate = source.IndexOf("disintegrating opponents", StringComparison.OrdinalIgnoreCase) >= 0;
            if (hasDisintegrate)
            {
                return $"체력과 매지카에 초당 {magToken}의 전격 피해를 입히며, 일정 확률로 적을 분해하는 {nameKo}입니다.";
            }
            return $"체력과 매지카에 초당 {magToken}의 전격 피해를 입히는 {nameKo}입니다.";
        }

        return null;
    }
}
