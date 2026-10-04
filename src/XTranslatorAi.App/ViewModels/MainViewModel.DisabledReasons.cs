using System.ComponentModel;
using System.Linq;

namespace XTranslatorAi.App.ViewModels;

/// <summary>
/// Disabled buttons gave no reason: "ESP 저장" turned off for an unreadable record that was reported only once while
/// opening, "XML 내보내기" was simply grey in a plugin project, and the translation editor ignored typing during a run.
/// These tooltips (shown on disabled controls) say why.
/// </summary>
public partial class MainViewModel
{
    public string ExportPluginToolTip
    {
        get
        {
            if (_projectState.PluginDocument is { } document)
            {
                var blocking = document.Info.Diagnostics.Where(d => d.BlocksExport).Select(d => d.Message).ToList();
                if (blocking.Count > 0)
                {
                    return "이 플러그인은 저장할 수 없습니다: " + string.Join(" / ", blocking.Take(3));
                }
            }
            else if (_projectState.XmlInfo != null)
            {
                return "XML 프로젝트입니다. 'XML 내보내기'를 쓰세요.";
            }

            if (IsTranslating)
            {
                return "번역 중에는 저장할 수 없습니다. 번역을 멈추거나 끝난 뒤에 저장하세요.";
            }

            return "새 출력 폴더에 원본 플러그인 이름을 유지하여 저장합니다.";
        }
    }

    public string ExportXmlToolTip
        => _projectState.PluginDocument != null
            ? "플러그인(ESP) 프로젝트입니다. 'ESP 저장'을 쓰세요."
            : IsTranslating
                ? "번역 중에는 내보낼 수 없습니다. 번역을 멈추거나 끝난 뒤에 내보내세요."
                : "번역한 xTranslator XML을 내보냅니다.";

    public string DestEditorToolTip
        => IsTranslating ? "번역 중에는 번역문을 고칠 수 없습니다. 번역을 멈추거나 끝난 뒤에 고치세요." : "번역문을 고치고 Ctrl+S로 저장하거나 다른 행을 고르면 저장됩니다.";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsTranslating) or nameof(IsProjectLoaded) or nameof(IsWorkspaceInteractive))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ExportPluginToolTip)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ExportXmlToolTip)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(DestEditorToolTip)));
        }
    }
}
