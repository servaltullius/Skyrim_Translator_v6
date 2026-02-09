using System;

namespace XTranslatorAi.Core.Xml;

public sealed record XTranslatorXmlInfo(
    string AddonName,
    string SourceLang,
    string DestLang,
    string Version,
    bool HasBom,
    string PrologLine
);

public sealed record XTranslatorXmlStringRow(
    int OrderIndex,
    string? ListAttr,
    string? PartialAttr,
    string? AttributesJson,
    string? Edid,
    string? Rec,
    string SourceText,
    string DestText,
    Models.StringEntryStatus Status,
    string RawStringXml
);

