using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{

    private async Task RefreshTmHitFlagsAsync(CancellationToken cancellationToken)
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        var notes = await db.GetStringNotesByKindAsync(TranslationConstants.TmHitNoteKind, cancellationToken).ConfigureAwait(false);
        var hitIds = new HashSet<long>(notes.Keys);

        await DispatchAsync(
            () =>
            {
                foreach (var vm in Entries)
                {
                    vm.IsTranslationMemoryApplied = hitIds.Contains(vm.Id);
                }
            }
        );
    }
}
