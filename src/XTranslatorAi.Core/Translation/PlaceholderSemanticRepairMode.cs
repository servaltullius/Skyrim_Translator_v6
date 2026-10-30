namespace XTranslatorAi.Core.Translation;

public enum PlaceholderSemanticRepairMode
{
    Off = 0,
    Soft = 1,
    Strict = 2,
}


public static class PlaceholderSemanticRepair
{
    /// <summary>The placeholder check a run makes: none without the repair pass (the run and the agent's answer check alike).</summary>
    public static PlaceholderSemanticRepairMode Effective(bool enableRepairPass, PlaceholderSemanticRepairMode mode)
        => enableRepairPass ? mode : PlaceholderSemanticRepairMode.Off;
}
