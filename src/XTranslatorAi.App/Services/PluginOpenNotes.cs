using System;
using System.Collections.Generic;
using System.Linq;

namespace XTranslatorAi.App.Services;

/// <summary>
/// What an open of a plugin tells, the same in the GUI's status line and the agent's open. The agent's open said only the
/// glossary it inherited and the encoding: a project continued from a moved plugin, the translations kept for rows whose
/// English changed, and a diagnostic that stops the export came out only at the export, or never.
/// </summary>
public static class PluginOpenNotes
{
    /// <param name="Code">The plugin diagnostic's code; null for a note of the project.</param>
    public sealed record Note(string Text, bool BlocksExport = false, string? Code = null);

    public static IReadOnlyList<Note> Describe(ProjectWorkspaceService.LoadFromPluginResult loaded)
    {
        var notes = new List<Note>();
        if (loaded.MovedFromPath != null) notes.Add(new($"옮기기 전 위치({loaded.MovedFromPath})의 프로젝트를 이어서 엽니다"));
        if (loaded.ContinuedFromTargetEncoding != null) notes.Add(new($"출력 인코딩 {loaded.ContinuedFromTargetEncoding}로 작업하던 프로젝트를 이어서 엽니다"));
        if (loaded.ContinuedAfterReadSettingsChange) notes.Add(new("Strings 폴더나 메타데이터 인코딩을 바꾸기 전의 프로젝트를 이어서 엽니다"));
        if (loaded.InheritedGlossary.Count > 0)
        {
            notes.Add(new($"같은 모드의 다른 프로젝트({string.Join(", ", (loaded.InheritedGlossary.FromPlugins ?? Array.Empty<string>()).Take(3))})에서 "
                          + $"용어 {loaded.InheritedGlossary.Count}개를 가져왔습니다"));
        }

        if (loaded.RetiredTranslations > 0)
        {
            notes.Add(new($"원문이 바뀌었거나 없어진 행의 번역 {loaded.RetiredTranslations}개를 보관해 두었습니다(같은 원문이 다시 나오면 되살립니다)"));
        }

        notes.AddRange(loaded.Document.Info.Diagnostics.Select(item => new Note(item.Message, item.BlocksExport, item.Code)));
        return notes;
    }
}
