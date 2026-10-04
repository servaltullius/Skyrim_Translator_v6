using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    /// <summary>
    /// Finds the names of the run in its rows and the project's translated rows, and takes their spellings from
    /// the translated rows (see <see cref="RunNameMemory"/>).
    /// </summary>
    private async Task InitializeRunNameMemoryAsync(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items,
        CancellationToken cancellationToken
    )
    {
        var translated = await _db.GetTranslatedPairsAsync(cancellationToken);
        var names = RunNameMemory.Build(items.Select(i => i.Source).Concat(translated.Select(r => r.SourceText)));
        names.Preload(translated);
        Ctx.RunNames = names.NameCount > 0 ? names : null;
    }
}
