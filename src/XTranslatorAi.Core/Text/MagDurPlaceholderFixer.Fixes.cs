using System;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static string TryFixWithTemplates(string source, string dest)
    {
        var trimmedSource = source.Trim();

        return TryFixRestoreAndBasicEffectTemplates(trimmedSource)
            ?? TryFixProjectileAndBeamTemplates(trimmedSource)
            ?? TryFixDrainAndCreatureTemplates(trimmedSource)
            ?? TryFixCloakTemplates(trimmedSource)
            ?? TryFixAbsorbAndShockTemplates(source, trimmedSource)
            ?? TryFixFortifiesAndWeakensTemplates(trimmedSource)
            ?? TryFixLossAndRecoveryMirrorTemplates(trimmedSource)
            ?? TryFixBadMagDurUsageTemplates(trimmedSource, dest)
            ?? dest;
    }
}
