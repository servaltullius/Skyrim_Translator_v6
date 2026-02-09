using XTranslatorAi.App.ViewModels.Tabs;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    public StringsTabViewModel StringsTab { get; }
    public CompareTabViewModel CompareTab { get; }
    public LqaTabViewModel LqaTab { get; }
    public ProjectGlossaryTabViewModel ProjectGlossaryTab { get; }
    public GlobalGlossaryTabViewModel GlobalGlossaryTab { get; }
    public GlobalTranslationMemoryTabViewModel GlobalTranslationMemoryTab { get; }
    public PromptTabViewModel PromptTab { get; }
    public ProjectContextTabViewModel ProjectContextTab { get; }
    public ApiLogsTabViewModel ApiLogsTab { get; }
}
