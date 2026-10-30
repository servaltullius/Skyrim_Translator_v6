using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private string BuildSystemPrompt()
    {
        // The custom prompt as agent mode reads it (SystemPromptBuilder), from the settings the window shows.
        return _systemPromptBuilder.Build(BasePromptText, WithTranslationPreferences(new AppSettings()),
            EnableProjectContext ? ProjectContextPreview : null);
    }

    private async Task<IReadOnlyList<GlossaryEntry>?> TryLoadGlobalGlossaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _globalGlossaryService.TryGetDbAsync(cancellationToken) == null)
            {
                return null;
            }

            return await _globalGlossaryService.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return null;
        }
    }

    /// <summary>The series TM with its original casing, for the official-name index (as TranslationRunnerService loads it).</summary>
    private async Task<IReadOnlyList<(string Source, string Target)>?> TryLoadReferenceNameMemoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await _globalTranslationMemoryService.GetEntriesAsync(SourceLang.Trim(), TargetLang.Trim(), cancellationToken);
            return entries.Select(entry => (entry.SourceText, entry.DestText)).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> TryLoadFranchiseTranslationMemoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _globalTranslationMemoryService.TryGetDbAsync(cancellationToken) == null)
            {
                return null;
            }

            return await _globalTranslationMemoryService.GetDictionaryAsync(SourceLang.Trim(), TargetLang.Trim(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return null;
        }
    }
}
