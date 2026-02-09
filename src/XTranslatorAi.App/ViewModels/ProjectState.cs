using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using XTranslatorAi.App.Collections;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Xml;

namespace XTranslatorAi.App.ViewModels;

public sealed class ProjectState
{
    public ProjectDb? Db { get; private set; }
    public XTranslatorXmlInfo? XmlInfo { get; private set; }
    public string? InputXmlPath { get; private set; }

    public ObservableRangeCollection<StringEntryViewModel> Entries { get; } = new();

    private readonly Dictionary<long, StringEntryViewModel> _byId = new();

    public string CurrentXmlFileName
        => string.IsNullOrWhiteSpace(InputXmlPath) ? "" : Path.GetFileName(InputXmlPath);

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
        Db = null;
    }

    public async Task DisposeDbAsync()
    {
        if (Db != null)
        {
            await Db.DisposeAsync();
            Db = null;
        }
    }

    public void SetWorkspace(ProjectDb db, XTranslatorXmlInfo xmlInfo, string inputXmlPath)
    {
        Db = db;
        XmlInfo = xmlInfo;
        InputXmlPath = inputXmlPath;
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
