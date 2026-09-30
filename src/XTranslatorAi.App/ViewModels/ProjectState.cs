using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using XTranslatorAi.App.Collections;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Xml;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.ViewModels;

public sealed class ProjectState
{
    public ProjectDb? Db { get; private set; }
    public XTranslatorXmlInfo? XmlInfo { get; private set; }
    public string? InputXmlPath { get; private set; }
    public PluginDocument? PluginDocument { get; private set; }
    public string? PluginTargetEncoding { get; private set; }
    public string? InputPath => PluginDocument?.Info.InputPath ?? InputXmlPath;
    public string? AddonName => PluginDocument == null ? XmlInfo?.AddonName : Path.GetFileName(PluginDocument.Info.InputPath);
    public bool HasSource => XmlInfo != null || PluginDocument != null;

    public ObservableRangeCollection<StringEntryViewModel> Entries { get; } = new();

    private readonly Dictionary<long, StringEntryViewModel> _byId = new();

    public string CurrentXmlFileName
        => string.IsNullOrWhiteSpace(InputPath) ? "" : Path.GetFileName(InputPath);

    public bool TryGetById(long id, out StringEntryViewModel entry)
    {
        if (_byId.TryGetValue(id, out var found) && found != null)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    public void Clear()
    {
        Entries.Clear();
        _byId.Clear();
        XmlInfo = null;
        InputXmlPath = null;
        PluginDocument = null;
        PluginTargetEncoding = null;
        Db = null;
    }

    public async Task DisposeDbAsync()
    {
        var db = Db;
        Db = null;
        if (db != null)
        {
            await db.DisposeAsync();
        }
    }

    public void SetWorkspace(ProjectDb db, XTranslatorXmlInfo xmlInfo, string inputXmlPath)
    {
        Db = db;
        XmlInfo = xmlInfo;
        InputXmlPath = inputXmlPath;
        PluginDocument = null;
        PluginTargetEncoding = null;
    }

    public void SetPluginWorkspace(ProjectDb db, PluginDocument document, string targetEncoding)
    {
        Db = db;
        PluginDocument = document;
        PluginTargetEncoding = targetEncoding;
        XmlInfo = null;
        InputXmlPath = null;
    }

    public void SetEntries(IReadOnlyList<StringEntryViewModel> entries)
    {
        Entries.ReplaceAll(entries);
        _byId.Clear();
        foreach (var e in entries)
        {
            _byId[e.Id] = e;
        }
    }
}
