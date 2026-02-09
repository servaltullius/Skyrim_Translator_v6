using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Xml;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    public string CurrentXmlFileName
        => _projectState.CurrentXmlFileName;

    [RelayCommand]
    private async Task OpenXmlAsync()
    {
        StopTranslation();

        var xmlPath = PromptOpenXmlPath();
        if (xmlPath == null)
        {
            return;
        }

        StatusMessage = "Importing XML...";
        await DisposeProjectDbAsync();
        ResetProjectState();

        try
        {
            await LoadProjectFromXmlAsync(xmlPath);

            IsProjectLoaded = true;
            StatusMessage = $"Loaded {TotalCount} strings from {Path.GetFileName(xmlPath)}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("XML 로드", ex);
            ResetProjectState();
            await DisposeProjectDbAsync();
        }
    }

    private string? PromptOpenXmlPath()
        => _uiInteractionService.ShowOpenFileDialog(
            new OpenFileDialogRequest(
                Filter: "xTranslator XML (*.xml)|*.xml|All files (*.*)|*.*",
                Title: "Open xTranslator XML"
            )
        );

    private void ResetProjectState()
    {
        IsProjectLoaded = false;
        _projectState.Clear();
        OnPropertyChanged(nameof(CurrentXmlFileName));
        ProjectContextPreview = "";
    }

    private async Task DisposeProjectDbAsync()
    {
        await _projectState.DisposeDbAsync();
    }

    private async Task LoadProjectFromXmlAsync(string xmlPath)
    {
        var result = await _projectWorkspaceService.LoadFromXmlAsync(
            new ProjectWorkspaceService.LoadFromXmlRequest(
                XmlPath: xmlPath,
                SelectedFranchise: SelectedFranchise,
                SelectedModel: SelectedModel,
                CustomPromptText: CustomPromptText,
                UseCustomPrompt: UseCustomPrompt
            ),
            CancellationToken.None
        );

        _projectState.SetWorkspace(result.Db, result.XmlInfo, result.InputXmlPath);
        OnPropertyChanged(nameof(CurrentXmlFileName));

        SelectedFranchise = result.Franchise;
        SourceLang = result.SourceLang;
        TargetLang = result.TargetLang;

        await TryAutoImportGlobalTranslationMemoryAsync();

        await ReloadGlossaryAsync();
        await ReloadGlobalGlossaryAsync();
        await ReloadGlobalTranslationMemoryAsync();
        await ReloadProjectContextAsync();
        await LoadEntriesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportXmlAsync()
    {
        var db = _projectState.Db;
        var xmlInfo = _projectState.XmlInfo;
        if (db == null || xmlInfo == null)
        {
            return;
        }

        var exportPath = _uiInteractionService.ShowSaveFileDialog(
            new SaveFileDialogRequest(
                Filter: "xTranslator XML (*.xml)|*.xml|All files (*.*)|*.*",
                Title: "Export translated XML",
                FileName: Path.GetFileNameWithoutExtension(xmlInfo.AddonName) + ".translated.xml"
            )
        );
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return;
        }

        try
        {
            StatusMessage = "Exporting XML...";
            await _projectWorkspaceService.ExportXmlAsync(db, xmlInfo, exportPath, CancellationToken.None);
            StatusMessage = $"Exported: {exportPath}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("XML Export", ex);
        }
    }

    private bool CanExport() => IsProjectLoaded && !IsTranslating;

    [RelayCommand(CanExecute = nameof(CanSaveSelectedDest))]
    private async Task SaveSelectedDestAsync()
    {
        if (SelectedEntry == null)
        {
            return;
        }

        try
        {
            await CommitDestEditAsync(SelectedEntry, SelectedEntry.DestText);
            StatusMessage = "Saved Dest edit.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Dest 저장", ex);
        }
    }

    private bool CanSaveSelectedDest() => IsProjectLoaded && !IsTranslating && SelectedEntry != null;

    public async Task CommitDestEditAsync(StringEntryViewModel entry, string newDest)
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        entry.DestText = newDest ?? "";
        entry.Status = StringEntryStatus.Edited;
        entry.IsTranslationMemoryApplied = false;
        await db.UpdateStringTranslationAsync(entry.Id, entry.DestText, StringEntryStatus.Edited, null, CancellationToken.None);
    }

    private async Task LoadEntriesAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        var total = (int)Math.Min(int.MaxValue, await db.GetStringCountAsync(CancellationToken.None));
        TotalCount = total;

        var tmHitIds = new System.Collections.Generic.HashSet<long>(
            (await db.GetStringNotesByKindAsync(TmHitNoteKind, CancellationToken.None)).Keys
        );

        const int pageSize = 500;
        var loaded = new List<StringEntryViewModel>(capacity: total);
        var done = 0;
        var pending = 0;

        for (var offset = 0; offset < total; offset += pageSize)
        {
            var rows = await db.GetStringsAsync(pageSize, offset, CancellationToken.None);
            foreach (var row in rows)
            {
                var vm = new StringEntryViewModel(row.Id, row.OrderIndex)
                {
                    Edid = row.Edid,
                    Rec = row.Rec,
                    SourceText = row.SourceText,
                    DestText = row.DestText,
                    Status = row.Status,
                    ErrorMessage = row.ErrorMessage,
                    IsTranslationMemoryApplied = tmHitIds.Contains(row.Id),
                };

                loaded.Add(vm);

                if (row.Status == StringEntryStatus.Done)
                {
                    done++;
                }
                if (row.Status == StringEntryStatus.Pending)
                {
                    pending++;
                }
            }
        }

        _projectState.SetEntries(loaded);
        DoneCount = done;
        PendingCount = pending;
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
