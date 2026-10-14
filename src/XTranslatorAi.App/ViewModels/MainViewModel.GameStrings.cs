using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private readonly GameStringService? _gameStringService;

    [ObservableProperty]
    private string _gameStringStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildGameStringDictionaryCommand))]
    private bool _isBuildingGameStrings;

    private bool CanBuildGameStringDictionary() => _gameStringService != null && !IsBuildingGameStrings;

    /// <summary>
    /// Builds the player's game translation dictionary from the game's Data folder (found in Steam, or picked by its
    /// Skyrim.esm) and the Korean patch's string tables (picked by Skyrim_english.STRINGS).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuildGameStringDictionary))]
    private async Task BuildGameStringDictionaryAsync()
    {
        if (_gameStringService == null)
        {
            return;
        }

        var dataFolder = GameStringService.DetectGameDataFolder();
        if (dataFolder == null)
        {
            var esm = _uiInteractionService.ShowOpenFileDialog(new OpenFileDialogRequest(
                "Skyrim.esm|Skyrim.esm", "스카이림 SE의 Data 폴더에서 Skyrim.esm을 고르세요", "Skyrim.esm"));
            if (esm == null)
            {
                return;
            }

            dataFolder = Path.GetDirectoryName(esm)!;
        }

        var koreanTable = _uiInteractionService.ShowOpenFileDialog(new OpenFileDialogRequest(
            "Skyrim_english.STRINGS|Skyrim_english.STRINGS", "한글패치의 Strings 폴더에서 Skyrim_english.STRINGS를 고르세요",
            "Skyrim_english.STRINGS"));
        if (koreanTable == null)
        {
            return;
        }

        IsBuildingGameStrings = true;
        GameStringStatus = "게임 번역 사전을 만드는 중...";
        try
        {
            var report = await _gameStringService.BuildAsync(dataFolder, Path.GetDirectoryName(koreanTable)!, CancellationToken.None);
            GameStringStatus = await _gameStringService.DescribeAsync(CancellationToken.None);
            StatusMessage = report.Skipped.Count == 0
                ? $"게임 번역 사전을 만들었습니다: {string.Join(", ", report.Plugins)}"
                : $"게임 번역 사전을 만들었습니다: {string.Join(", ", report.Plugins)} (없어서 뺀 것: {string.Join(", ", report.Skipped)})";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            GameStringStatus = await _gameStringService.DescribeAsync(CancellationToken.None);
            _uiInteractionService.ShowMessage(ex.Message, "게임 번역 사전", UiMessageBoxButton.Ok, UiMessageBoxImage.Warning);
        }
        finally
        {
            IsBuildingGameStrings = false;
        }
    }

    private async Task RefreshGameStringStatusAsync()
    {
        if (_gameStringService == null)
        {
            return;
        }

        try
        {
            GameStringStatus = await _gameStringService.DescribeAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            GameStringStatus = "게임 번역 사전을 읽지 못했습니다: " + ex.Message;
        }
    }

    // The dictionary pairs English game strings with the Korean patch, so it only serves Skyrim English → Korean.
    private Task<GameStringDictionary?> TryLoadGameStringsAsync(CancellationToken cancellationToken)
        => _gameStringService != null && SelectedFranchise == BethesdaFranchise.ElderScrolls
           && string.Equals((SourceLang ?? "").Trim(), "english", StringComparison.OrdinalIgnoreCase)
           && LanguageHelper.IsKoreanLanguage(TargetLang)
            ? _gameStringService.TryLoadAsync(cancellationToken)
            : Task.FromResult<GameStringDictionary?>(null);
}
