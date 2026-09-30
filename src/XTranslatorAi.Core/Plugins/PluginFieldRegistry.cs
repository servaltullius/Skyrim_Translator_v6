using System.Collections.Generic;

namespace XTranslatorAi.Core.Plugins;

/// <summary>
/// The table used when the plugin is localized. A null table denotes a field that
/// remains an inline string even in a localized plugin.
/// </summary>
public sealed record PluginFieldRule(PluginStringTableKind? LocalizedTable, bool RequiresNonEmpty = true);

/// <summary>
/// Skyrim SE display fields, not a general list of string-shaped binary data.
/// Record identities and field meanings were checked against xEdit TES5 definitions
/// (9058a79437367c2cb1b757a9021ab443d743012d), xTranslator SkyrimSE record definitions
/// (9aa38d60860273401f8bb0dd1557c82a295c41b3), and Mutagen Skyrim schemas
/// (5d69a2d99d1ae1917e23db67f3e323b306168538). These are independently expressed data
/// mappings; no parser or writer implementation from those projects is incorporated.
/// </summary>
public static class PluginFieldRegistry
{
    private static readonly PluginFieldRule Normal = new(PluginStringTableKind.Strings);
    private static readonly PluginFieldRule Description = new(PluginStringTableKind.DlStrings);
    private static readonly PluginFieldRule Dialogue = new(PluginStringTableKind.IlStrings);

    // Knowing a record signature does not imply that every subrecord in it is a
    // translation field, or that a new/unknown field can be discarded on export.
    private static readonly HashSet<string> KnownRecords = new(StringComparer.Ordinal)
    {
        "AACT", "ACHR", "ACTI", "ADDN", "ALCH", "AMMO", "ANIO", "APPA", "ARMA", "ARMO",
        "ARTO", "ASPC", "ASTP", "AVIF", "BOOK", "BPTD", "CAMS", "CELL", "CLAS", "CLDC",
        "CLFM", "CLMT", "COBJ", "COLL", "CONT", "CPTH", "CSTY", "DEBR", "DIAL", "DLBR",
        "DLVW", "DOBJ", "DOOR", "DUAL", "ECZN", "EFSH", "ENCH", "EQUP", "EXPL", "EYES",
        "FACT", "FLOR", "FLST", "FSTP", "FSTS", "FURN", "GLOB", "GMST", "GRAS", "HAIR",
        "HAZD", "HDPT", "IDLE", "IDLM", "IMAD", "IMGS", "INFO", "INGR", "IPCT", "IPDS",
        "KEYM", "KYWD", "LAND", "LCRT", "LCTN", "LENS", "LGTM", "LIGH", "LSCR", "LTEX",
        "LVLI", "LVLN", "LVSP", "MATO", "MATT", "MESG", "MGEF", "MISC", "MOVT", "MSTT",
        "MUSC", "MUST", "NAVI", "NAVM", "NPC_", "OTFT", "PACK", "PARW", "PBAR", "PBEA",
        "PCON", "PERK", "PFLA", "PGRE", "PHZD", "PLYR", "PMIS", "PROJ", "PWAT", "QUST",
        "RACE", "REFR", "REGN", "RELA", "REVB", "RFCT", "RGDL", "SCEN", "SCOL", "SCPT",
        "SCRL", "SHOU", "SLGM", "SMBN", "SMEN", "SMQN", "SNCT", "SNDR", "SOPM", "SOUN",
        "SPEL", "SPGD", "STAT", "TACT", "TES4", "TREE", "TXST", "VOLI", "VTYP", "WATR",
        "WEAP", "WOOP", "WRLD", "WTHR",
    };

    private static readonly HashSet<string> NamedRecords = new(StringComparer.Ordinal)
    {
        "ACTI", "ALCH", "AMMO", "APPA", "ARMO", "AVIF", "BOOK", "CELL", "CLAS", "CLFM",
        "CONT", "DIAL", "DOOR", "ENCH", "EXPL", "EYES", "FACT", "FLOR", "FURN", "HAZD",
        "HDPT", "INGR", "KEYM", "LCTN", "LIGH", "MESG", "MGEF", "MISC", "MSTT", "NPC_",
        "PERK", "PROJ", "QUST", "RACE", "REFR", "SCRL", "SHOU", "SLGM", "SNCT", "SPEL",
        "TACT", "TREE", "WATR", "WEAP", "WOOP", "WRLD",
    };

    private static readonly HashSet<string> DescribedRecords = new(StringComparer.Ordinal)
    {
        "ALCH", "AMMO", "APPA", "ARMO", "AVIF", "BOOK", "CLAS", "COLL", "MESG", "PERK",
        "RACE", "SCRL", "SHOU", "SPEL", "WEAP",
    };

    public static bool IsKnownRecord(string recordType) => KnownRecords.Contains(recordType);

    public static PluginFieldRule? GetRule(
        string recordType,
        string subrecordType,
        string? edid,
        IReadOnlyList<PluginSubrecordDescriptor> precedingSubrecords)
    {
        ArgumentNullException.ThrowIfNull(precedingSubrecords);

        if (subrecordType == "FULL" && NamedRecords.Contains(recordType))
        {
            return Normal;
        }

        if (subrecordType == "DESC")
        {
            // Load screens use STRINGS, unlike the other DESC fields.
            if (recordType == "LSCR") return Normal;
            if (DescribedRecords.Contains(recordType)) return Description;
        }

        return (recordType, subrecordType) switch
        {
            ("MGEF", "DNAM") => Normal,
            ("INFO", "NAM1") => Dialogue,
            ("INFO", "RNAM") => Normal,
            ("NPC_", "SHRT") => Normal,
            ("QUST", "CNAM") => Description,
            ("QUST", "NNAM") when IsQuestObjective(precedingSubrecords) => Normal,
            ("BOOK", "CNAM") => Description,
            ("WOOP", "TNAM") => Normal,
            ("MESG", "ITXT") => Normal,
            ("REGN", "RDMP") => Normal,
            ("ACTI", "RNAM") => Normal,
            ("FLOR", "RNAM") => Normal,
            ("BPTD", "BPTN") => Normal,
            ("FACT", "MNAM") => Normal,
            ("FACT", "FNAM") => Normal,
            ("GMST", "DATA") when edid is { Length: > 0 } && edid[0] == 's' => Normal,
            ("PERK", "EPF2") when GetPerkParameterType(precedingSubrecords) == 4 => Normal,
            ("PERK", "EPFD") => GetPerkParameterType(precedingSubrecords) switch
            {
                7 => Normal,
                // EPFT=6 (Select Text) is a non-translated engine identifier,
                // e.g. Set Boolean Graph Variable -> bPerkShieldCharge. Its
                // string storage does not make it player-visible prose.
                _ => null, // Other variants contain identifiers, numbers or FormIDs.
            },
            _ => null,
        };
    }

    private static bool IsQuestObjective(IReadOnlyList<PluginSubrecordDescriptor> preceding)
    {
        for (var i = preceding.Count - 1; i >= 0; i--)
        {
            switch (preceding[i].Type)
            {
                case "QOBJ":
                    return true;
                case "ANAM":
                case "ALST":
                case "ALLS":
                case "ALED":
                case "INDX":
                    return false;
            }
        }

        // A trailing, root-level QUST NNAM is not objective text. xEdit describes
        // it as a plain developer description while Mutagen specifies DLSTRINGS.
        // Do not guess a table from its byte length or translate it as an objective.
        return false;
    }

    private static byte? GetPerkParameterType(IReadOnlyList<PluginSubrecordDescriptor> preceding)
    {
        byte? parameterType = null;
        for (var i = preceding.Count - 1; i >= 0; i--)
        {
            var subrecord = preceding[i];
            if (subrecord.Type == "PRKF") return null;
            if (subrecord.Type == "PRKE")
            {
                // Only entry point effects have typed function parameters.
                return subrecord.Data.Length == 3 && subrecord.Data.Span[0] == 2
                    ? parameterType
                    : null;
            }

            if (subrecord.Type == "EPFT" && parameterType is null)
            {
                if (subrecord.Data.Length != 1) return null;
                parameterType = subrecord.Data.Span[0];
            }
        }

        return null;
    }
}
