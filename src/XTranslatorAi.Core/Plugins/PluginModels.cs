using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Plugins;

public enum PluginGame { SkyrimSpecialEdition }
public enum PluginStringTableKind { Strings, DlStrings, IlStrings }

public sealed record PluginSubrecordDescriptor(string Type, ReadOnlyMemory<byte> Data);

public sealed record PluginReadOptions(
    PluginGame Game = PluginGame.SkyrimSpecialEdition,
    string SourceLanguage = "english",
    string SourceEncoding = "utf-8",
    string? StringsDirectory = null,
    IReadOnlyList<string>? ArchivePaths = null,
    string MetadataEncoding = "windows-1252");

/// <summary>Identity points at the original record and repeated subrecord occurrence, never at source text alone.</summary>
public sealed record PluginField(
    string Key, int OrderIndex, string RecordType, string SubrecordType,
    uint FormId, string? EditorId, int RecordIndex, int SubrecordIndex,
    string SourceText, PluginStringTableKind? TableKind = null, uint? StringId = null, bool RequiresNonEmpty = true,
    uint? DialogueTopicFormId = null)
{
    public string Rec => RecordType + ":" + SubrecordType;
}

public sealed record PluginDiagnostic(string Code, string Message, bool BlocksExport = false);

public sealed record PluginSourceInfo(
    string InputPath, string Sha256, PluginReadOptions Options, bool IsLocalized,
    IReadOnlyList<string> Masters, IReadOnlyList<PluginDiagnostic> Diagnostics,
    IReadOnlyDictionary<PluginStringTableKind, string>? StringTableSources = null);

/// <param name="AllowDroppedInitialsAndPronouns">For translations carried from an existing translated release: the English
/// initial's picture (Illuminated_Letters) and the English pronoun the game fills in (Alias.Pronoun...) may be left out.</param>
public sealed record PluginExportOptions(string OutputDirectory, string TargetEncoding = "utf-8", string? OutputLanguage = null,
    bool AllowDroppedInitialsAndPronouns = false);

public sealed record PluginExportResult(string PluginPath, IReadOnlyList<string> Files, int ChangedFields, string Sha256);
