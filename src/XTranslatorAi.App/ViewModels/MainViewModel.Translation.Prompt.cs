using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private string BuildSystemPrompt()
    {
        return _systemPromptBuilder.Build(
            basePrompt: BasePromptText,
            useCustomPrompt: UseCustomPrompt,
            customPromptText: CustomPromptText,
            enableProjectContext: EnableProjectContext,
            projectContext: ProjectContextPreview
        );
    }

    private async Task<IReadOnlyList<GlossaryEntry>?> TryLoadGlobalGlossaryAsync()
    {
        try
        {
            if (await _globalGlossaryService.TryGetDbAsync(CancellationToken.None) == null)
            {
                return null;
            }

            return await _globalGlossaryService.GetAsync(CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> TryLoadGlobalTranslationMemoryAsync()
    {
        try
        {
            if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
            {
                return null;
            }

            return await _globalTranslationMemoryService.GetDictionaryAsync(SourceLang.Trim(), TargetLang.Trim(), CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }
}
